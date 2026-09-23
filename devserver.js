// Minimal local stand-in for `vercel dev`: serves the published Blazor output
// (out/wwwroot) and routes /api/<name> to api/<name>.js, matching Vercel's
// (req, res) handler contract.
//
//   dotnet publish src/HmlEnvDb/HmlEnvDb.csproj -c Release -o out
//   node devserver.js          → http://localhost:4173
const http = require('http');
const fs = require('fs');
const path = require('path');

const ROOT = path.join(__dirname, 'out', 'wwwroot');
const TYPES = {
  '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css',
  '.json': 'application/json', '.png': 'image/png', '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon', '.wasm': 'application/wasm', '.dat': 'application/octet-stream',
  '.dll': 'application/octet-stream', '.blat': 'application/octet-stream',
};

http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  let p = decodeURIComponent(url.pathname);

  if (p.startsWith('/api/')) {
    const name = p.slice(5).replace(/[^a-z0-9_-]/gi, '');
    const file = path.join(__dirname, 'api', `${name}.js`);
    if (!fs.existsSync(file)) { res.writeHead(404).end('no such api'); return; }
    // Shim the bits of Vercel's res that handlers use.
    res.status = c => { res.statusCode = c; return res; };
    res.json = j => { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(j)); };
    res.send = t => { res.end(t); };
    try {
      delete require.cache[require.resolve(file)];
      await require(file)(Object.assign(req, { query: Object.fromEntries(url.searchParams) }), res);
    } catch (e) {
      console.error(`[api/${name}]`, e);
      if (!res.headersSent) res.status(500).json({ error: String(e.message || e) });
    }
    return;
  }

  if (p === '/') p = '/index.html';
  const file = path.join(ROOT, p);
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) {
    res.writeHead(404).end('not found'); return;
  }
  res.setHeader('Content-Type', TYPES[path.extname(file)] || 'application/octet-stream');
  fs.createReadStream(file).pipe(res);
}).listen(4173, () => console.log('dev server on http://localhost:4173'));
