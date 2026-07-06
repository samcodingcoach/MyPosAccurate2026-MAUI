using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Microsoft.Maui.Graphics;
using System.Globalization;
using System.IO;

namespace MyPosAccurate2026.Coa;

public partial class Detail : ContentPage
{
    ObservableCollection<NeracaGroup> _neracaGroups = new ObservableCollection<NeracaGroup>();
    List<NeracaItem> _allNeracaItems = new List<NeracaItem>();
    bool _isFetching = false;
    bool _loaded = false;
    string _asOfDate = "";
    CancellationTokenSource _searchCts;
    private CompanyProfileData _company;

    public Detail()
    {
        InitializeComponent();
        CV_Neraca.ItemsSource = _neracaGroups;
        _asOfDate = DateTime.Now.ToString("dd/MM/yyyy");
        LabelDate.Text = $"PER TANGGAL {_asOfDate}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            _loaded = true;
            await LoadNeraca();
            await LoadCompanyAsync();
        }
    }

    private async Task LoadNeraca()
    {
        if (_isFetching) return;
        _isFetching = true;
        MainThread.BeginInvokeOnMainThread(() => RV_Neraca.IsRefreshing = true);

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
                return;
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

                string apiUrl = $"{App.API_HOST}coa/saldo-neraca.php";
                if (!string.IsNullOrEmpty(_asOfDate))
                {
                    apiUrl += $"?asOfDate={Uri.EscapeDataString(_asOfDate)}";
                }

                var response = await client.GetAsync(apiUrl);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (responseContent.StartsWith("<"))
                {
                    await DisplayAlertAsync("Error Server", "Gagal membaca format data dari server.", "OK");
                    return;
                }

                var result = JsonConvert.DeserializeObject<SaldoNeracaResponse>(responseContent);
                if (result?.status == "success" && result.data != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        LabelTotalAset.Text = result.summary != null 
                            ? (result.summary.totalAset >= 0 ? $"Rp {result.summary.totalAset:N0}" : $"-Rp {Math.Abs(result.summary.totalAset):N0}") 
                            : "Rp 0";
                        LabelSelisih.Text = result.summary != null 
                            ? (result.summary.selisih >= 0 ? $"Rp {result.summary.selisih:N0}" : $"-Rp {Math.Abs(result.summary.selisih):N0}") 
                            : "Rp 0";
                        LabelDate.Text = $"PER TANGGAL {_asOfDate}";

                        _allNeracaItems.Clear();
                        foreach (var kvp in result.data)
                        {
                            if (kvp.Value.items != null)
                            {
                                _allNeracaItems.AddRange(kvp.Value.items);
                            }
                        }

                        ApplyFilter();
                    });
                }
                else
                {
                    await DisplayAlertAsync("Gagal", result?.message ?? "Gagal mengambil data neraca.", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat data neraca: {ex.Message}");
        }
        finally
        {
            _isFetching = false;
            MainThread.BeginInvokeOnMainThread(() => RV_Neraca.IsRefreshing = false);
        }
    }

    private void ApplyFilter()
    {
        _neracaGroups.Clear();
        string search = (T_Search.Text ?? "").Trim().ToLower();

        var filteredItems = _allNeracaItems.Where(x => 
            string.IsNullOrEmpty(search) || 
            (x.accountName != null && x.accountName.ToLower().Contains(search)) || 
            (x.accountNo != null && x.accountNo.ToLower().Contains(search))
        ).ToList();

        // Build hierarchical models
        var parents = filteredItems.Where(x => x.isParent || x.lvl == 1).ToList();
        var allChildren = filteredItems.Where(x => !x.isParent && x.lvl > 1).ToList();
        
        var hierarchicalParents = new List<NeracaItem>();
        foreach(var p in parents)
        {
            p.Children = new ObservableCollection<NeracaItem>(allChildren.Where(c => c.parentNo == p.accountNo));
            hierarchicalParents.Add(p);
        }

        var asetTypes = new[] { "CASH_BANK", "ACCOUNT_RECEIVABLE", "INVENTORY", "OTHER_CURRENT_ASSET", "FIXED_ASSET", "ACCUMULATED_DEPRECIATION", "OTHER_ASSET" };
        var liabilitasTypes = new[] { "ACCOUNT_PAYABLE", "OTHER_CURRENT_LIABILITY", "LONG_TERM_LIABILITY" };
        
        var aset = hierarchicalParents.Where(x => asetTypes.Contains(x.accountType)).ToList();
        var liab = hierarchicalParents.Where(x => liabilitasTypes.Contains(x.accountType)).ToList();
        var ekuit = hierarchicalParents.Where(x => !asetTypes.Contains(x.accountType) && !liabilitasTypes.Contains(x.accountType)).ToList();

        if (aset.Count > 0) _neracaGroups.Add(new NeracaGroup("ASET AKTIVA", aset.Sum(x => x.amount), aset, "DarkCyan"));
        if (liab.Count > 0) _neracaGroups.Add(new NeracaGroup("LIABILITAS & HUTANG", liab.Sum(x => x.amount), liab, "HotPink"));
        if (ekuit.Count > 0) _neracaGroups.Add(new NeracaGroup("EKUITAS & MODAL", ekuit.Sum(x => x.amount), ekuit, "Gold"));
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadNeraca();
    }

    private async void T_Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchCts != null)
            _searchCts.Cancel();
            
        _searchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(500, _searchCts.Token);
            ApplyFilter();
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async void TapAdd_Tapped(object sender, TappedEventArgs e)
    {
        string result = await DisplayPromptAsync("Filter Tanggal", "Masukkan tanggal (DD/MM/YYYY)", initialValue: _asOfDate, keyboard: Keyboard.Text);
        if (!string.IsNullOrWhiteSpace(result))
        {
            _asOfDate = result.Trim();
            await LoadNeraca();
        }
    }

    private string ReportFileName()
    {
        return $"Laporan-Neraca-{_asOfDate.Replace("/", "")}.pdf";
    }

    private async void BtnDownloadPdf_Clicked(object sender, EventArgs e)
    {
        if (_neracaGroups.Count == 0)
        {
            await DisplayAlertAsync("Data Kosong", "Tidak ada data neraca untuk didownload.", "OK");
            return;
        }

#if ANDROID
        try
        {
            byte[] pdfBytes = await Task.Run(() => BuildReportPdfAndroid());
            string filePath = Path.Combine(FileSystem.CacheDirectory, ReportFileName());
            File.WriteAllBytes(filePath, pdfBytes);

            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Simpan / Bagikan Laporan Neraca PDF",
                File = new ShareFile(filePath)
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Gagal", "Gagal membuat PDF: " + ex.Message, "OK");
        }
#else
        await DisplayAlertAsync("Tidak Didukung", "Pembuatan PDF laporan hanya didukung di Android.", "OK");
#endif
    }

#if ANDROID
    private byte[] BuildReportPdfAndroid()
    {
        // A4 portrait @72dpi
        const int pageW = 595;
        const int pageH = 842;
        const float margin = 40f;
        float contentTop = margin;
        float contentBottom = pageH - margin;

        var cBlack = Android.Graphics.Color.Argb(255, 26, 26, 26);
        var cValue = Android.Graphics.Color.Argb(255, 51, 51, 51);
        var cGrey = Android.Graphics.Color.Argb(255, 119, 119, 119);
        var cBand = Android.Graphics.Color.Argb(255, 232, 232, 232);
        var cLine = Android.Graphics.Color.Argb(255, 51, 51, 51);
        var cSoft = Android.Graphics.Color.Argb(255, 204, 204, 204);

        var pCompany = new Android.Graphics.Paint { Color = cBlack, TextSize = 18, FakeBoldText = true, AntiAlias = true };
        var pCompanyInfo = new Android.Graphics.Paint { Color = cGrey, TextSize = 10, AntiAlias = true };
        var pTitle = new Android.Graphics.Paint { Color = cBlack, TextSize = 15, FakeBoldText = true, AntiAlias = true };
        var pPeriode = new Android.Graphics.Paint { Color = cGrey, TextSize = 11, AntiAlias = true, TextSkewX = -0.25f };
        var pBody = new Android.Graphics.Paint { Color = cValue, TextSize = 11, AntiAlias = true };
        var pBodyB = new Android.Graphics.Paint { Color = cBlack, TextSize = 11, FakeBoldText = true, AntiAlias = true };
        var pGroup = new Android.Graphics.Paint { Color = cBlack, TextSize = 13, FakeBoldText = true, AntiAlias = true };
        var pFootDate = new Android.Graphics.Paint { Color = cGrey, TextSize = 9, AntiAlias = true };
        var pBg = new Android.Graphics.Paint { AntiAlias = true };
        var pLinePaint = new Android.Graphics.Paint { Color = cLine, StrokeWidth = 1f, AntiAlias = true };
        var pSoftLine = new Android.Graphics.Paint { Color = cSoft, StrokeWidth = 1f, AntiAlias = true };

        float xLeft = margin;
        float xRightTotal = pageW - margin;

        float TextH(Android.Graphics.Paint p) => p.Descent() - p.Ascent();
        float Baseline(float top, Android.Graphics.Paint p) => top - p.Ascent();

        var blocks = new List<(float h, Action<Android.Graphics.Canvas, float> draw)>();

        void AddCenter(string text, Android.Graphics.Paint p, float gap)
        {
            if (string.IsNullOrEmpty(text)) return;
            float h = TextH(p) + gap;
            blocks.Add((h, (canvas, top) =>
            {
                float w = p.MeasureText(text);
                canvas.DrawText(text, (pageW - w) / 2f, Baseline(top, p), p);
            }));
        }
        void AddLeft(string text, Android.Graphics.Paint p, float gap, float paddingLeft = 0)
        {
            float h = TextH(p) + gap;
            blocks.Add((h, (canvas, top) => canvas.DrawText(text ?? "", xLeft + paddingLeft, Baseline(top, p), p)));
        }
        void AddRow2(string a, string val, Android.Graphics.Paint pa, Android.Graphics.Paint pv, float gap, float paddingLeft = 0)
        {
            float h = TextH(pa) + gap;
            blocks.Add((h, (canvas, top) =>
            {
                float bl = Baseline(top, pa);
                canvas.DrawText(a ?? "", xLeft + paddingLeft, bl, pa);
                if (val != null) canvas.DrawText(val, xRightTotal - pv.MeasureText(val), bl, pv);
            }));
        }
        void AddBand(string label, string val, float gap)
        {
            float bandH = 26f;
            float h = bandH + gap;
            blocks.Add((h, (canvas, top) =>
            {
                pBg.Color = cBand;
                canvas.DrawRect(new Android.Graphics.RectF(xLeft, top, xRightTotal, top + bandH), pBg);
                float bl = top + bandH / 2f - (pBodyB.Ascent() + pBodyB.Descent()) / 2f;
                canvas.DrawText(label, xLeft + 10, bl, pBodyB);
                if (val != null) canvas.DrawText(val, xRightTotal - 10 - pBodyB.MeasureText(val), bl, pBodyB);
            }));
        }
        void AddSeparator(float gapAbove, float gapBelow, bool soft = false)
        {
            float h = gapAbove + gapBelow + 1;
            blocks.Add((h, (canvas, top) =>
            {
                float ly = top + gapAbove;
                canvas.DrawLine(xLeft, ly, xRightTotal, ly, soft ? pSoftLine : pLinePaint);
            }));
        }
        void AddSpacer(float height) => blocks.Add((height, (canvas, top) => { }));

        if (_company != null)
        {
            AddCenter(_company.name, pCompany, 4);
            AddCenter(_company.address, pCompanyInfo, 2);
            AddCenter(ComposeRegion(_company), pCompanyInfo, 2);
            AddCenter(ComposeContact(_company), pCompanyInfo, 6);
            AddSeparator(2, 14, soft: true);
        }

        AddCenter("Laporan Neraca", pTitle, 4);
        AddCenter($"Per Tanggal {_asOfDate}", pPeriode, 12);
        AddSeparator(2, 10);

        foreach (var group in _neracaGroups)
        {
            AddRow2(group.Name, group.TotalFormatted, pGroup, pGroup, 8);
            AddSeparator(2, 6, soft: true);

            foreach (var item in group)
            {
                AddRow2(item.accountName, item.amount_fmt, pBodyB, pBodyB, 4, 10);
                if (item.Children != null)
                {
                    foreach(var child in item.Children)
                    {
                        AddRow2(child.accountName, child.amount_fmt, pBody, pBody, 4, 30);
                    }
                }
                AddSpacer(4);
            }
            AddSpacer(8);
        }

        AddSeparator(2, 10);
        string selisihText = LabelSelisih.Text;
        string totalAsetText = LabelTotalAset.Text;
        AddBand("TOTAL ASET", totalAsetText, 4);
        AddSpacer(4);
        AddBand("SELISIH", selisihText, 4);

        AddSpacer(16);
        AddCenter($"Dicetak pada: {DateTime.Now.ToString("dd MMM yyyy HH:mm:ss", new CultureInfo("id-ID"))}", pFootDate, 0);

        using var document = new Android.Graphics.Pdf.PdfDocument();
        int pageNum = 1;
        var page = document.StartPage(new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(pageW, pageH, pageNum).Create());
        var canvasPage = page.Canvas;
        canvasPage.DrawColor(Android.Graphics.Color.White);
        float y = contentTop;

        foreach (var (h, draw) in blocks)
        {
            if (y + h > contentBottom)
            {
                document.FinishPage(page);
                pageNum++;
                page = document.StartPage(new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(pageW, pageH, pageNum).Create());
                canvasPage = page.Canvas;
                canvasPage.DrawColor(Android.Graphics.Color.White);
                y = contentTop;
            }
            draw(canvasPage, y);
            y += h;
        }

        document.FinishPage(page);
        using var ms = new MemoryStream();
        document.WriteTo(ms);
        document.Close();
        return ms.ToArray();
    }
#endif

    private async Task LoadCompanyAsync()
    {
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken)) return;

            string apiUrl = $"{App.API_HOST}profile/company.php";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            var response = await client.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode) return;

            string responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseContent) || responseContent.TrimStart().StartsWith("<"))
                return;

            var apiResult = JsonConvert.DeserializeObject<CompanyProfileResponse>(responseContent);
            _company = apiResult?.data;
        }
        catch { }
    }

    private static string ComposeRegion(CompanyProfileData c)
    {
        if (c?.district == null) return null;
        var parts = new[] { c.district.district, c.district.province, c.district.country }.Where(s => !string.IsNullOrWhiteSpace(s));
        string region = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(region) ? null : region;
    }

    private static string ComposeContact(CompanyProfileData c)
    {
        if (c == null) return null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.phone)) parts.Add($"Telp: {c.phone}");
        if (!string.IsNullOrWhiteSpace(c.email)) parts.Add($"Email: {c.email}");
        return parts.Count > 0 ? string.Join("  •  ", parts) : null;
    }
}

public class CompanyProfileResponse { public string status { get; set; } public CompanyProfileData data { get; set; } }
public class CompanyProfileData { public string name { get; set; } public string email { get; set; } public string address { get; set; } public string phone { get; set; } public CompanyDistrict district { get; set; } }
public class CompanyDistrict { public string country { get; set; } public string province { get; set; } public string district { get; set; } }

public class SaldoNeracaResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public Summary summary { get; set; }
    public Dictionary<string, CategoryData> data { get; set; }
}

public class Summary
{
    public double totalAset { get; set; }
    public double totalLiabilitasEkuitas { get; set; }
    public double selisih { get; set; }
}

public class CategoryData
{
    public double total { get; set; }
    public List<NeracaItem> items { get; set; }
}

public class NeracaItem
{
    public string parentNo { get; set; }
    public double amount { get; set; }
    public int lvl { get; set; }
    public bool isParent { get; set; }
    public string accountName { get; set; }
    public string accountNo { get; set; }
    public string accountType { get; set; }

    public string amount_fmt => amount >= 0 ? $"Rp {amount:N0}" : $"-Rp {Math.Abs(amount):N0}";
    
    // Bindable children collection
    public ObservableCollection<NeracaItem> Children { get; set; }
}

public class NeracaGroup : ObservableCollection<NeracaItem>
{
    public string Name { get; set; }
    public string TotalFormatted { get; set; }
    public double TotalAmount { get; set; }
    public string TitleColor { get; set; }

    public NeracaGroup(string name, double total, IEnumerable<NeracaItem> items, string color = "DarkCyan") : base(items)
    {
        Name = name.ToUpper();
        TotalAmount = total;
        TotalFormatted = total >= 0 ? $"Rp {total:N0}" : $"-Rp {Math.Abs(total):N0}";
        TitleColor = color;
    }
}