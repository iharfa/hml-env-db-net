using Microsoft.JSInterop;

namespace HmlEnvDb;

// Thin wrapper over the DOM helpers in wwwroot/app-interop.js. Every visual
// update funnels through here; the interop layer stays dumb on purpose.
public static class Dom
{
    private static IJSRuntime _js = default!;
    public static void Init(IJSRuntime js) => _js = js;

    private static async ValueTask Call(string fn, params object?[] args)
    {
        try { await _js.InvokeVoidAsync("dash." + fn, args); }
        catch (Exception e) { Console.WriteLine($"dash.{fn} failed: {e.Message}"); }
    }

    public static ValueTask SetHtml(string id, string html) => Call("setHtml", id, html);
    public static ValueTask SetText(string id, string text) => Call("setText", id, text);
    public static ValueTask SetTextSel(string sel, string text) => Call("setTextSel", sel, text);
    public static ValueTask SetDisplaySel(string sel, bool show) => Call("setDisplaySel", sel, show);
    public static ValueTask ReconcileStationSection() => Call("reconcileStationSection");
    public static ValueTask SetClass(string id, string cls) => Call("setClass", id, cls);
    public static ValueTask SetDisplay(string id, bool show) => Call("setDisplay", id, show);
    public static ValueTask SetAttr(string id, string attr, string val) => Call("setAttr", id, attr, val);
    public static ValueTask ToggleClass(string id, string cls, bool on) => Call("toggleClass", id, cls, on);
    public static ValueTask InsertTideNote(string html) => Call("insertTideNote", html);
    public static ValueTask RenderChart(string id, string optionJson, string? ctxJson = null) => Call("renderChart", id, optionJson, ctxJson);
    public static ValueTask RenderHeroViz(string dataJson) => Call("renderHeroViz", dataJson);
    public static ValueTask FillOutlookCard(string id, string title, string msg, string tag, string badgeClass, bool show)
        => Call("fillOutlookCard", id, title, msg, tag, badgeClass, show);
    public static ValueTask DownloadCsv(string filename, string csv) => Call("downloadCsv", filename, csv);
    public static ValueTask SetLoading(bool loading) => Call("setLoading", loading);
    public static ValueTask WireEvents() => Call("wireEvents");

    public static async ValueTask<bool> IsHidden(string id)
    {
        try { return await _js.InvokeAsync<bool>("dash.isHidden", id); }
        catch { return true; }
    }
}
