using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.IO;

namespace MyPosAccurate2026.Coa;

public partial class RugiLaba : ContentPage
{
    ObservableCollection<RugiLabaGroup> _rugiLabaGroups = new ObservableCollection<RugiLabaGroup>();
    bool _isFetching = false;
    bool _loaded = false;

    public RugiLaba()
    {
        InitializeComponent();
        CV_RugiLaba.ItemsSource = _rugiLabaGroups;
        
        var today = DateTime.Today;
        DP_StartDate.Date = new DateTime(today.Year, today.Month, 1);
        DP_EndDate.Date = today;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            _loaded = true;
            await LoadRugiLaba();
            await LoadCompanyAsync();
        }
    }

    private async Task LoadRugiLaba()
    {
        if (_isFetching) return;
        _isFetching = true;
        MainThread.BeginInvokeOnMainThread(() => RV_RugiLaba.IsRefreshing = true);

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
                string fromDateStr = $"{DP_StartDate.Date:dd/MM/yyyy}";
                string toDateStr = $"{DP_EndDate.Date:dd/MM/yyyy}";

                string apiUrl = $"{App.API_HOST}coa/rugilaba.php?fromDate={Uri.EscapeDataString(fromDateStr)}&toDate={Uri.EscapeDataString(toDateStr)}";

                var response = await client.GetAsync(apiUrl);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (responseContent.StartsWith("<"))
                {
                    await DisplayAlertAsync("Error Server", "Gagal membaca format data dari server.", "OK");
                    return;
                }

                var result = JsonConvert.DeserializeObject<RugiLabaResponse>(responseContent);
                if (result?.status == "success")
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (result.summary != null)
                        {
                            LblLabaBersih.Text = FormatCurrency(result.summary.labaBersih);
                            LblTotalPendapatan.Text = FormatCurrency(result.summary.totalPendapatan);
                            LblHPP.Text = FormatCurrency(result.summary.hpp);
                            LblLabaKotor.Text = FormatCurrency(result.summary.labaKotor);
                            LblBebanOperasional.Text = FormatCurrency(result.summary.bebanOperasional);
                            LblPendapatanLain.Text = FormatCurrency(result.summary.pendapatanLainLain);
                            LblBebanLain.Text = FormatCurrency(result.summary.bebanLainLain);
                        }

                        _rugiLabaGroups.Clear();
                        if (result.data != null)
                        {
                            AddGroup("PENDAPATAN", result.data.revenue, "DarkCyan");
                            AddGroup("BEBAN POKOK PENJUALAN", result.data.cogs, "Firebrick");
                            AddGroup("BEBAN OPERASIONAL", result.data.expense, "OrangeRed");
                            AddGroup("PENDAPATAN LAINNYA", result.data.other_income, "MediumSeaGreen");
                            AddGroup("BEBAN LAINNYA", result.data.other_expense, "Crimson");
                            AddGroup("LAINNYA", result.data.lainnya, "DimGray");
                        }
                    });
                }
                else
                {
                    await DisplayAlertAsync("Gagal", result?.message ?? "Gagal mengambil data rugi laba.", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gagal memuat data rugi laba: {ex.Message}");
        }
        finally
        {
            _isFetching = false;
            MainThread.BeginInvokeOnMainThread(() => RV_RugiLaba.IsRefreshing = false);
        }
    }

    private void AddGroup(string title, RugiLabaCategory category, string color)
    {
        if (category == null)
            return;

        var parents = category.items?.Where(x => x.isParent || x.lvl == 1).ToList() ?? new List<RugiLabaItem>();
        var allChildren = category.items?.Where(x => !x.isParent && x.lvl > 1).ToList() ?? new List<RugiLabaItem>();
        
        var hierarchicalParents = new List<RugiLabaItem>();
        foreach(var p in parents)
        {
            p.Children = new ObservableCollection<RugiLabaItem>(allChildren.Where(c => c.parentNo == p.accountNo));
            hierarchicalParents.Add(p);
        }

        _rugiLabaGroups.Add(new RugiLabaGroup(title, category.total, hierarchicalParents, color));
    }

    private string FormatCurrency(double amount)
    {
        return amount >= 0 ? $"Rp {amount:N0}" : $"-Rp {Math.Abs(amount):N0}";
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadRugiLaba();
    }

    private async void BtnSearch_Tapped(object sender, TappedEventArgs e)
    {
        await LoadRugiLaba();
    }

    private void LabaBersih_Tapped(object sender, TappedEventArgs e)
    {
        bool isExpanded = GridSummaryDetails.IsVisible;
        GridSummaryDetails.IsVisible = !isExpanded;
        LblExpanderIcon.Text = isExpanded ? "▼" : "▲";
    }

    private CompanyProfileData _company;

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

    private string ReportFileName()
    {
        return $"Laporan-RugiLaba-{DateTime.Now:yyyyMMddHHmmss}.pdf";
    }

    private async void BtnDownloadPdf_Clicked(object sender, EventArgs e)
    {
        if (_rugiLabaGroups.Count == 0)
        {
            await DisplayAlertAsync("Data Kosong", "Tidak ada data rugi laba untuk didownload.", "OK");
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
                Title = "Simpan / Bagikan Laporan Rugi Laba PDF",
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

        AddCenter("Laporan Laba Rugi", pTitle, 4);
        AddCenter($"Periode: {$"{DP_StartDate.Date:dd/MM/yyyy}"} - {$"{DP_EndDate.Date:dd/MM/yyyy}"}", pPeriode, 12);
        AddSeparator(2, 10);

        foreach (var group in _rugiLabaGroups)
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
        AddRow2("Total Pendapatan", LblTotalPendapatan.Text, pGroup, pGroup, 6, 0);
        AddRow2("HPP", LblHPP.Text, pGroup, pGroup, 6, 0);
        AddRow2("Laba Kotor", LblLabaKotor.Text, pGroup, pGroup, 6, 0);
        AddRow2("Beban Operasional", LblBebanOperasional.Text, pGroup, pGroup, 6, 0);
        AddRow2("Pendapatan Lainnya", LblPendapatanLain.Text, pGroup, pGroup, 6, 0);
        AddRow2("Beban Lainnya", LblBebanLain.Text, pGroup, pGroup, 6, 0);
        AddSpacer(8);
        
        AddBand("LABA BERSIH", LblLabaBersih.Text, 4);

        AddSpacer(16);
        AddCenter($"Dicetak pada: {DateTime.Now.ToString("dd MMM yyyy HH:mm:ss", new System.Globalization.CultureInfo("id-ID"))}", pFootDate, 0);

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
}

public class RugiLabaResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public RugiLabaSummary summary { get; set; }
    public RugiLabaData data { get; set; }
}

public class RugiLabaSummary
{
    public double totalPendapatan { get; set; }
    public double hpp { get; set; }
    public double labaKotor { get; set; }
    public double bebanOperasional { get; set; }
    public double labaOperasional { get; set; }
    public double pendapatanLainLain { get; set; }
    public double bebanLainLain { get; set; }
    public double labaBersih { get; set; }
}

public class RugiLabaData
{
    public RugiLabaCategory revenue { get; set; }
    public RugiLabaCategory cogs { get; set; }
    public RugiLabaCategory expense { get; set; }
    
    [JsonProperty("other-income")]
    public RugiLabaCategory other_income { get; set; }
    
    [JsonProperty("other-expense")]
    public RugiLabaCategory other_expense { get; set; }
    
    public RugiLabaCategory lainnya { get; set; }
}

public class RugiLabaCategory
{
    public double total { get; set; }
    public List<RugiLabaItem> items { get; set; }
}

public class RugiLabaItem
{
    public int id { get; set; }
    
    public string accountNo { get; set; }
    public string accountName { get; set; }
    
    [JsonProperty("no")]
    public string no { set { accountNo = value; } }
    
    [JsonProperty("name")]
    public string name { set { accountName = value; } }
    
    public string accountType { get; set; }
    public double amount { get; set; }
    public int lvl { get; set; }
    public bool isParent { get; set; }
    public string parentNo { get; set; }

    public string amount_fmt => amount >= 0 ? $"Rp {amount:N0}" : $"-Rp {Math.Abs(amount):N0}";
    
    public ObservableCollection<RugiLabaItem> Children { get; set; }
}

public class RugiLabaGroup : ObservableCollection<RugiLabaItem>
{
    public string Name { get; set; }
    public string TotalFormatted { get; set; }
    public double TotalAmount { get; set; }
    public string TitleColor { get; set; }

    public RugiLabaGroup(string name, double total, IEnumerable<RugiLabaItem> items, string color) : base(items)
    {
        Name = name.ToUpper();
        TotalAmount = total;
        TotalFormatted = total >= 0 ? $"Rp {total:N0}" : $"-Rp {Math.Abs(total):N0}";
        TitleColor = color;
    }
}