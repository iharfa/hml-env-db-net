'use strict';

/* ════════════════════════════════════════════════════════════
   HULHUMALÉ ENVIRONMENTAL DASHBOARD — interop layer
   All data fetching, parsing and page logic lives in C# (Blazor
   WebAssembly). This file is the thin presentation glue that C#
   cannot reach directly: the ECharts JS library and
   small DOM utilities.
════════════════════════════════════════════════════════════ */

(function () {

  const ASSEMBLY = 'HmlEnvDb';

  // ── formatting helpers (mirrors of the C# Fmt class, needed only because
  //    ECharts formatter callbacks must be JS functions) ──
  const MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
  const DAYS = ['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];

  function formatNumber(v, digits = 1) {
    if (v === null || v === undefined || isNaN(v)) return '—';
    return Number(v).toFixed(digits);
  }
  function formatTime(iso) {
    if (!iso || iso.length < 16) return '—';
    const h = parseInt(iso.slice(11, 13), 10), m = parseInt(iso.slice(14, 16), 10);
    if (isNaN(h) || isNaN(m)) return iso;
    const ap = h < 12 ? 'AM' : 'PM';
    const hr = h % 12 === 0 ? 12 : h % 12;
    return `${String(hr).padStart(2, '0')}:${String(m).padStart(2, '0')} ${ap}`;
  }
  function formatDate(iso) {
    if (!iso) return '—';
    const d = new Date(iso.slice(0, 10) + 'T00:00:00');
    if (isNaN(d)) return iso;
    return `${DAYS[d.getDay()]}, ${MONTHS[d.getMonth()]} ${d.getDate()}`;
  }
  function formatHourLabel(iso) {
    if (!iso) return '';
    const h = parseInt(iso.slice(11, 13), 10);
    if (isNaN(h)) return '';
    const ampm = h < 12 ? 'AM' : 'PM';
    const hr = h % 12 === 0 ? 12 : h % 12;
    return `${hr} ${ampm}`;
  }
  const formatDayLabel = iso => iso ? formatDate(iso.slice(0, 10)) : '—';
  const formatDateTimeLabel = iso => `${formatDayLabel(iso)} · ${formatTime(iso)}`;
  function aqiColor(v) {
    if (v == null || isNaN(v)) return '#94a3b8';
    if (v <= 50) return '#16a34a'; if (v <= 100) return '#ca8a04'; if (v <= 150) return '#ea580c';
    if (v <= 200) return '#dc2626'; if (v <= 300) return '#7c3aed'; return '#7e0023';
  }

  // ════════════════════════════════════════════════════════════
  //  ECharts bridge — options come from C# as JSON with "@fn:NAME"
  //  tokens wherever a JS callback is required.
  // ════════════════════════════════════════════════════════════

  const charts = {};

  function getOrCreateChart(id) {
    const el = document.getElementById(id);
    if (!el || typeof echarts === 'undefined') return null;
    if (charts[id] && !charts[id].isDisposed()) charts[id].dispose();
    charts[id] = echarts.init(el, null, { renderer: 'canvas' });
    return charts[id];
  }

  // Each entry is a factory: (ctx, arg) => actual ECharts callback.
  const FORMATTERS = {
    lineTooltip: () => p =>
      `${formatDateTimeLabel(p[0].axisValue)}<br/>${p.map(s => `${s.seriesName}: ${s.value ?? '—'}`).join('<br/>')}`,
    hourlyInterval: (ctx, step) => (index, value) => index === 0
      || (value.slice(14, 16) === '00' && parseInt(value.slice(11, 13), 10) % step === 0),
    hourlyAxisLabel: () => (value, index) => {
      const time = formatHourLabel(value);
      return index === 0 ? `{t|${time}}\n{d|${formatDayLabel(value)}}` : `{t|${time}}`;
    },
    tideTooltip: () => p => `${formatDateTimeLabel(p[0].axisValue)}<br/>${formatNumber(p[0].value, 2)} m`,
    tideMarkLabel: () => p => `${p.name}  ${formatNumber(p.data.coord[1], 2)} m`,
    fixed1: () => v => `${Number(v).toFixed(1)}`,
    rain5dTooltip: () => params =>
      `${params[0].name}<br/>Rain: ${params[0].value} mm<br/>Prob: ${params[1]?.value ?? '—'}%`,
    dhwTooltip: () => p =>
      `Day ${p[0].name}<br/>DHW: ${p[0].value} °C-weeks<br/>SST: ${p[1]?.value ?? '—'} °C`,
    dhwBarColor: () => p => {
      const v = p.value;
      if (v >= 8) return '#7e0023';
      if (v >= 4) return '#dc2626';
      if (v >= 1) return '#ea580c';
      return '#16a34a';
    },
    agHistTooltip: ctx => p => {
      const d = ctx.daily[p[0].dataIndex];
      return `${formatDate(d.date)}<br/>AQI avg <b>${d.aqiAvg}</b> (peak ${d.aqiMax})`
        + `<br/>PM2.5 ${d.pm25Avg} µg/m³ (${d.pm25Min}–${d.pm25Max})<br/>${d.n} samples`;
    },
    agAxisLabel: () => v => formatDate(v),
    aqiBarColor: () => p => aqiColor(p.value),
  };

  // Recursively swap "@fn:NAME[:ARG]" strings for registry callbacks.
  function hydrate(node, ctx) {
    if (typeof node === 'string' && node.startsWith('@fn:')) {
      const rest = node.slice(4);
      const ci = rest.indexOf(':');
      const name = ci === -1 ? rest : rest.slice(0, ci);
      let arg = ci === -1 ? undefined : rest.slice(ci + 1);
      if (arg !== undefined) { try { arg = JSON.parse(arg); } catch { /* keep string */ } }
      const factory = FORMATTERS[name];
      return factory ? factory(ctx, arg) : node;
    }
    if (Array.isArray(node)) return node.map(x => hydrate(x, ctx));
    if (node && typeof node === 'object') {
      for (const k of Object.keys(node)) node[k] = hydrate(node[k], ctx);
      return node;
    }
    return node;
  }

  // ════════════════════════════════════════════════════════════
  //  Daily-briefing hero visualisations (SVG/DOM drawing; the
  //  numbers arrive precomputed from C#)
  // ════════════════════════════════════════════════════════════

  const quad = (t, a, b, c) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;
  const clamp01 = v => Math.max(0, Math.min(1, v));

  // Moon disc: lit half on the waxing side + terminator ellipse of width |2f-1|.
  function paintMoon(el, f, waxing, size, litColor) {
    const k = Math.abs(2 * f - 1), gibbous = f > 0.5;
    const half = el.querySelector('.moon__half'); if (half) half.style.left = waxing ? '50%' : '0%';
    const term = el.querySelector('.moon__term'); if (!term) return;
    term.style.width = (k * size) + 'px';
    term.style.background = gibbous ? litColor : '#141b33';
  }

  function renderHeroViz(d) {
    const $ = id => document.getElementById(id);
    const set = (el, attr, val) => { if (el) el.setAttribute(attr, val); };
    const now = d.nowMinutes;

    if ($('aqiDot') && d.aqi != null) $('aqiDot').style.left = clamp01(d.aqi / 200) * 100 + '%';

    if ($('uvBars') && d.uvByHour) $('uvBars').innerHTML = d.uvByHour.map(v => {
      const c = v >= 8 ? '#e2564d' : v >= 6 ? '#f5813e' : v >= 3 ? '#e6c13f' : '#9dd3ae';
      return `<i style="height:${(10 + v * 2.2).toFixed(0)}px;background:${c}"></i>`;
    }).join('');

    if ($('rainBars') && d.rainByHour) $('rainBars').innerHTML = d.rainByHour.map(v => {
      const c = v > .5 ? '#3b82f6' : v > .1 ? '#8fb8f7' : '#dbe6f5';
      return `<i style="height:${Math.max(4, v * 34).toFixed(0)}px;background:${c}"></i>`;
    }).join('');

    if ($('windBars') && d.gustByHour) $('windBars').innerHTML = d.gustByHour.map(v => {
      const c = v >= 60 ? '#e2564d' : v >= 45 ? '#f5813e' : v >= 30 ? '#e6c13f' : '#9dd3ae';
      return `<i style="height:${Math.max(4, Math.min(34, v / 70 * 34)).toFixed(0)}px;background:${c}"></i>`;
    }).join('');
    if ($('windMeta') && d.windMeta) $('windMeta').textContent = d.windMeta;

    if ($('swell') && d.swell) $('swell').innerHTML = d.swell.map(([label, h, o]) =>
      `<div class="swell__col"><div class="swell__slot"><i style="height:${h}px;opacity:${o}"></i></div><span>${label}</span></div>`
    ).join('');
    if ($('seaMeta') && d.seaMeta) $('seaMeta').textContent = d.seaMeta;

    if ($('tideCurve')) {
      const tideAt = u => 22 - 12 * Math.cos(4 * Math.PI * u);
      let path = '';
      for (let i = 0; i <= 40; i++) { const u = i / 40; path += (i ? ' L ' : 'M ') + (200 * u).toFixed(1) + ' ' + tideAt(u).toFixed(2); }
      set($('tideCurve'), 'd', path);
      const prog = d.tideProgress == null ? 0.5 : d.tideProgress; const tx = 200 * prog;
      set($('tideRule'), 'x1', tx); set($('tideRule'), 'x2', tx);
      set($('tideDot'), 'cx', tx); set($('tideDot'), 'cy', tideAt(prog));
      if ($('tideLabelLeft')) $('tideLabelLeft').textContent = d.tideLeft || '';
      if ($('tideLabelRight')) $('tideLabelRight').textContent = d.tideRight || '';
    }

    if ($('sunTravelled') && d.sun && d.sun.set > d.sun.rise) {
      const ts = clamp01((now - d.sun.rise) / (d.sun.set - d.sun.rise));
      let sd = '';
      for (let i = 0; i <= 24; i++) { const t = ts * i / 24; sd += (i ? ' L ' : 'M ') + quad(t, 14, 160, 306).toFixed(1) + ' ' + quad(t, 96, -14, 96).toFixed(1); }
      set($('sunTravelled'), 'd', sd);
      const sx = quad(ts, 14, 160, 306), sy = quad(ts, 96, -14, 96);
      ['sunDot', 'sunGlow'].forEach(s => { set($(s), 'cx', sx); set($(s), 'cy', sy); });
      const left = Math.max(0, d.sun.set - now);
      if ($('daylightLeft')) $('daylightLeft').textContent = Math.floor(left / 60) + 'h ' + String(left % 60).padStart(2, '0') + 'm';
    }
    if ($('sunriseVal') && d.sunriseVal) $('sunriseVal').textContent = d.sunriseVal;
    if ($('sunsetVal') && d.sunsetVal) $('sunsetVal').textContent = d.sunsetVal;
    if ($('sunriseDir') && d.sunriseDir) $('sunriseDir').textContent = d.sunriseDir;
    if ($('sunsetDir') && d.sunsetDir) $('sunsetDir').textContent = d.sunsetDir;

    if (d.moon) {
      if ($('moonDot') && d.moon.rise != null && d.moon.set != null) {
        const mSet = d.moon.set < d.moon.rise ? d.moon.set + 1440 : d.moon.set;
        const nowM = now < d.moon.rise - 720 ? now + 1440 : now;
        const tm = clamp01((nowM - d.moon.rise) / (mSet - d.moon.rise));
        const mx = quad(tm, 10, 150, 290), my = quad(tm, 76, -18, 76);
        ['moonDot', 'moonGlow'].forEach(s => { set($(s), 'cx', mx); set($(s), 'cy', my); });
      }
      const f = d.moon.illumination != null ? d.moon.illumination : 0.5;
      const waxing = d.moon.waxing;
      if ($('moonDisc')) paintMoon($('moonDisc'), f, waxing, 92, '#efeada');
      if ($('phaseName')) $('phaseName').textContent = d.moon.phase;
      if ($('illumPct')) $('illumPct').textContent = Math.round(f * 100) + '% LIT';
      if ($('moonriseVal')) $('moonriseVal').textContent = d.moonriseVal || '—';
      if ($('moonsetVal')) $('moonsetVal').textContent = d.moonsetVal || '—';
      if ($('moonriseDir')) $('moonriseDir').textContent = d.moonriseDir || 'MOONRISE';
      if ($('moonsetDir')) $('moonsetDir').textContent = d.moonsetDir || 'MOONSET';

      const strip = [['NEW', 0, true], ['1ST Q', .5, true], ['TONIGHT', f, waxing], ['FULL', 1, true], ['LAST Q', .5, false]];
      const stripEl = $('phaseStrip');
      if (stripEl) {
        stripEl.innerHTML = strip.map(([label]) =>
          `<div class="phase-strip__item"><div class="moon"><span class="moon__half"></span><span class="moon__term"></span></div><span>${label}</span></div>`
        ).join('');
        stripEl.querySelectorAll('.phase-strip__item').forEach((item, i) => {
          const [label, ff, wax] = strip[i], disc = item.querySelector('.moon');
          disc.style.opacity = label === 'TONIGHT' ? 1 : .55;
          disc.style.boxShadow = label === 'TONIGHT' ? '0 0 0 2px #7c6cf0' : '0 0 0 1px rgba(21,34,56,.12)';
          paintMoon(disc, ff, wax, 22, '#d9d3c2');
        });
      }
    }
  }

  // ════════════════════════════════════════════════════════════
  //  Pollutant info modal (static reference content)
  // ════════════════════════════════════════════════════════════

  const POLLUTANT_INFO = {
    pm25: {
      symbol: 'PM2.5', fullName: 'Fine Particulate Matter',
      description: 'Tiny airborne particles 2.5 micrometres or smaller. Small enough to bypass the nose and throat and penetrate deep into the lungs and bloodstream.',
      health: 'Short-term exposure causes irritation of the eyes, nose, throat, and lungs. Long-term exposure increases risk of heart disease, lung cancer, and reduced lung function. Children, elderly, and people with asthma or heart conditions are most at risk.',
      sources: 'Vehicle exhaust, industrial emissions, biomass burning, cooking smoke, and secondary formation from gases reacting in the atmosphere.',
      who_guideline: '15 µg/m³ (annual mean)',
    },
    pm10: {
      symbol: 'PM10', fullName: 'Coarse Particulate Matter',
      description: 'Inhalable particles 10 micrometres or smaller, including dust, pollen, and mould spores. Larger than PM2.5 and generally filtered by the upper airways.',
      health: 'Can irritate the airways, trigger asthma and allergic reactions. People with pre-existing respiratory or cardiovascular conditions are at higher risk.',
      sources: 'Road dust, construction sites, agricultural activity, industrial processes, and sea spray.',
      who_guideline: '45 µg/m³ (annual mean)',
    },
    dust: {
      symbol: 'Dust', fullName: 'Atmospheric Mineral Dust',
      description: 'Mineral dust particles suspended in the atmosphere, typically originating from arid desert regions. Can travel thousands of kilometres on wind currents.',
      health: 'Can worsen respiratory conditions and reduce visibility. During high-dust events (e.g. Saharan or Arabian dust outbreaks), outdoor activity should be reduced for sensitive individuals.',
      sources: 'Desert and arid soil erosion (Sahara, Arabian Peninsula), volcanic ash, construction, and unpaved roads.',
      who_guideline: 'No separate WHO guideline — assessed via PM10/PM2.5.',
    },
    no2: {
      symbol: 'NO₂', fullName: 'Nitrogen Dioxide',
      description: 'A reddish-brown gas with a sharp odour. A key indicator of traffic-related air pollution and a precursor to both ozone and secondary PM2.5.',
      health: 'Irritates the respiratory tract at high concentrations. Chronic exposure is linked to development of asthma, and reduced lung growth in children. Also contributes to the formation of ground-level ozone.',
      sources: 'Road vehicle engines (especially diesel), power stations, shipping, and any high-temperature combustion process.',
      who_guideline: '10 µg/m³ (annual mean)',
    },
    ozone: {
      symbol: 'O₃', fullName: 'Ground-Level Ozone',
      description: 'A reactive gas formed when nitrogen oxides (NOₓ) and volatile organic compounds (VOCs) react in sunlight. Not directly emitted — formed in the atmosphere.',
      health: 'Causes chest pain, coughing, shortness of breath, and throat irritation. Can worsen bronchitis, emphysema, and asthma. Reduces lung function even in healthy adults. Peak levels typically occur on hot, sunny afternoons.',
      sources: 'Not emitted directly. Formed from reactions between NOₓ (traffic, industry) and VOCs (paints, solvents, vegetation) in sunlight.',
      who_guideline: '100 µg/m³ (8-hour mean)',
    },
    so2: {
      symbol: 'SO₂', fullName: 'Sulphur Dioxide',
      description: 'A colourless gas with a sharp, pungent smell. A major contributor to acid rain and a precursor to sulphate aerosol particles.',
      health: 'Irritates eyes, nose, and throat. High concentrations cause breathing difficulties and worsen asthma. Contributes to the formation of fine particles which cause additional health effects.',
      sources: 'Burning of fossil fuels containing sulphur (especially coal and oil), volcanic eruptions, metal smelting, and shipping fuel.',
      who_guideline: '40 µg/m³ (24-hour mean)',
    },
    co: {
      symbol: 'CO', fullName: 'Carbon Monoxide',
      description: 'A colourless, odourless, and tasteless gas produced by incomplete combustion of carbon-based fuels. Undetectable by human senses.',
      health: "Binds to haemoglobin more strongly than oxygen, reducing the blood's oxygen-carrying capacity. Symptoms include headaches, dizziness, confusion, and nausea. Very high concentrations can be fatal. Outdoor levels are rarely dangerous unless near heavy traffic or fires.",
      sources: 'Motor vehicle exhaust, residential heating (especially gas/wood), generators, industrial processes, and wildfires.',
      who_guideline: '4 mg/m³ (24-hour mean)',
    },
    uv: {
      symbol: 'UV Index', fullName: 'Ultraviolet (UV) Index',
      description: 'An international standard measurement of the strength of ultraviolet radiation from the sun at a given location and time. Ranges from 0 (minimal) to 11+ (extreme).',
      health: 'High UV causes sunburn in as little as 15 minutes. Long-term overexposure leads to skin aging, cataracts, and increased risk of skin cancer. The Maldives sits near the equator and regularly records UV indices of 10–12 — among the highest in the world.',
      sources: 'Solar radiation. Intensity depends on sun angle (higher at noon), cloud cover, altitude, ozone layer thickness, and reflective surfaces (water, sand).',
      who_guideline: 'Protective action recommended above UV Index 3.',
    },
  };

  window.showPollutantInfo = function (key) {
    const info = POLLUTANT_INFO[key];
    if (!info) return;
    let modal = document.getElementById('pollutant-modal');
    if (!modal) {
      modal = document.createElement('div');
      modal.id = 'pollutant-modal';
      modal.className = 'p-modal-overlay';
      modal.innerHTML = `
        <div class="p-modal-card" role="dialog" aria-modal="true" aria-labelledby="p-modal-title">
          <button class="p-modal-close" onclick="closePollutantInfo()" aria-label="Close">✕</button>
          <div class="p-modal-icon" id="p-modal-icon"></div>
          <div class="p-modal-symbol" id="p-modal-symbol"></div>
          <h3 class="p-modal-title" id="p-modal-title"></h3>
          <p class="p-modal-desc" id="p-modal-desc"></p>
          <div class="p-modal-section">
            <div class="p-modal-section-head">Health Effects</div>
            <p class="p-modal-text" id="p-modal-health"></p>
          </div>
          <div class="p-modal-section">
            <div class="p-modal-section-head">Common Sources</div>
            <p class="p-modal-text" id="p-modal-sources"></p>
          </div>
          <div class="p-modal-section">
            <div class="p-modal-section-head">WHO Air Quality Guideline</div>
            <p class="p-modal-text" id="p-modal-who"></p>
          </div>
        </div>`;
      modal.addEventListener('click', e => { if (e.target === modal) window.closePollutantInfo(); });
      document.body.appendChild(modal);
    }
    document.getElementById('p-modal-symbol').textContent = info.symbol;
    document.getElementById('p-modal-title').textContent = info.fullName;
    document.getElementById('p-modal-desc').textContent = info.description;
    document.getElementById('p-modal-health').textContent = info.health;
    document.getElementById('p-modal-sources').textContent = info.sources;
    document.getElementById('p-modal-who').textContent = info.who_guideline;
    modal.style.display = 'flex';
    document.body.style.overflow = 'hidden';
  };

  window.closePollutantInfo = function () {
    const modal = document.getElementById('pollutant-modal');
    if (modal) modal.style.display = 'none';
    document.body.style.overflow = '';
  };

  // ════════════════════════════════════════════════════════════
  //  Misc page plumbing
  // ════════════════════════════════════════════════════════════

  // The IQAir embed is whitelisted per-origin; skip the iframe on localhost.
  function initIqairMap() {
    if (document.getElementById('station-aq')?.hidden) return;
    const frame = document.getElementById('iqair-map');
    if (!frame) return;
    const h = location.hostname;
    const local = !h || h === 'localhost' || h === '127.0.0.1' || h.endsWith('.local');
    if (local) {
      frame.remove();
      const note = document.getElementById('iqair-map-note');
      if (note) note.innerHTML = 'The embedded national map loads on the deployed site only. '
        + '<a href="https://environment.gov.mv/en" target="_blank" rel="noopener">Open the national map ↗</a>';
    } else {
      frame.src = frame.dataset.src;
    }
  }

  function reconcileStationSection() {
    const hidden = el => !el || el.style.display === 'none';
    const ag = document.getElementById('aqicn-current');
    const iq = document.getElementById('iqair-current');
    const sources = document.getElementById('aqicn-sources');
    const history = document.querySelector('.aqicn-history-card');
    const head = document.querySelector('#section-aqi .subsection-head');
    const layout = document.querySelector('#section-aqi .aqicn-layout');

    const noSources = hidden(ag) && hidden(iq);
    if (sources) sources.style.display = noSources ? 'none' : '';
    if (layout) layout.style.gridTemplateColumns = noSources ? '1fr' : '';

    const nothing = noSources && hidden(history);
    if (head) head.style.display = nothing ? 'none' : '';
    if (layout) layout.style.display = nothing ? 'none' : '';
  }

  // ── window.dash: the API surface the C# Dom class calls ──
  window.dash = {
    setHtml(id, html) { const el = document.getElementById(id); if (el) el.innerHTML = html; },
    setText(id, text) { const el = document.getElementById(id); if (el) el.textContent = text; },
    setTextSel(sel, text) { const el = document.querySelector(sel); if (el) el.textContent = text; },
    setClass(id, cls) { const el = document.getElementById(id); if (el) el.className = cls; },
    setAttr(id, attr, val) { const el = document.getElementById(id); if (el) el.setAttribute(attr, val); },
    setDisplay(id, show) { const el = document.getElementById(id); if (el) el.style.display = show ? '' : 'none'; },
    setDisplaySel(sel, show) { const el = document.querySelector(sel); if (el) el.style.display = show ? '' : 'none'; },
    toggleClass(id, cls, on) { const el = document.getElementById(id); if (el) el.classList.toggle(cls, on); },
    isHidden(id) { const el = document.getElementById(id); return !el || el.hidden; },

    insertTideNote(html) {
      const grid = document.getElementById('marine-card-grid');
      if (!grid || grid.parentElement.querySelector('.tide-note')) return;
      const note = document.createElement('div');
      note.className = 'tide-note';
      note.innerHTML = html;
      grid.parentElement.insertBefore(note, grid);
    },

    fillOutlookCard(id, title, msg, tag, badgeClass, show) {
      const el = document.getElementById(id);
      if (!el) return;
      if (!show) { el.style.display = 'none'; return; }
      el.style.display = '';
      const h3 = el.querySelector('h3'); if (h3) h3.textContent = title;
      const p = el.querySelector('p'); if (p) p.textContent = msg;
      const b = el.querySelector('.badge');
      if (b && tag) { b.textContent = tag; b.className = badgeClass; }
    },

    renderChart(id, optionJson, ctxJson) {
      const chart = getOrCreateChart(id);
      if (!chart) return;
      const ctx = ctxJson ? JSON.parse(ctxJson) : null;
      chart.setOption(hydrate(JSON.parse(optionJson), ctx));
    },

    renderHeroViz(dataJson) { renderHeroViz(JSON.parse(dataJson)); },

    reconcileStationSection() { reconcileStationSection(); },

    downloadCsv(filename, csv) {
      const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = filename;
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    },

    setLoading(loading) {
      const btn = document.getElementById('btn-refresh');
      if (!btn) return;
      btn.classList.toggle('loading', loading);
      btn.setAttribute('aria-busy', loading ? 'true' : 'false');
    },

    wireEvents() {
      // Manual refresh button (re-entrancy is guarded on the C# side).
      const btn = document.getElementById('btn-refresh');
      if (btn) btn.addEventListener('click', () => DotNet.invokeMethodAsync(ASSEMBLY, 'LoadAll'));

      // AQICN history range selector + CSV export.
      document.querySelectorAll('#aqicn-range .range-btn').forEach(rbtn => {
        rbtn.addEventListener('click', () => {
          document.querySelectorAll('#aqicn-range .range-btn')
            .forEach(b => b.classList.toggle('is-active', b === rbtn));
          DotNet.invokeMethodAsync(ASSEMBLY, 'AgRange', Number(rbtn.dataset.range));
        });
      });
      const csvBtn = document.getElementById('aqicn-csv');
      if (csvBtn) csvBtn.addEventListener('click', () => DotNet.invokeMethodAsync(ASSEMBLY, 'AgCsv'));


      initIqairMap();

      // Window resize — resize all charts.
      window.addEventListener('resize', () => {
        Object.values(charts).forEach(c => { if (c && !c.isDisposed()) c.resize(); });
      });
    },
  };

})();
