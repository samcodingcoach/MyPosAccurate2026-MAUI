using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.IO;

namespace MyPosAccurate2026.Stok;

public partial class DetailwithInsert : ContentPage
{
    public string TransNumber { get; set; } = ""; 
    public string StatusName { get; set; } = "";
    private List<StokOpnameDetailItem> _allItems = new List<StokOpnameDetailItem>();

    public DetailwithInsert()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (StatusName == "Selesai")
        {
            BtnExportPdf.IsVisible = true;
        }
        await LoadData();
    }

    private async Task LoadData()
    {
        var delayTask = Task.Delay(3000);
        
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            if (string.IsNullOrEmpty(cleanToken))
            {
                await DisplayAlertAsync("Sesi Habis", "Anda harus login kembali.", "OK");
                return;
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            // Step 1: Hit stokopname-result/list.php?search={TransNumber}
            string searchUrl = $"{App.API_HOST}stokopname-result/list.php?search={Uri.EscapeDataString(TransNumber)}";
            var searchResponse = await client.GetAsync(searchUrl);
            
            if (!searchResponse.IsSuccessStatusCode)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("HTTP Error", $"Gagal mengambil daftar hasil (Status {searchResponse.StatusCode}).", "OK");
                });
                return;
            }

            var searchContent = await searchResponse.Content.ReadAsStringAsync();
            var searchResult = JsonConvert.DeserializeObject<StokOpnameResultListResponse>(searchContent);

            if (searchResult?.status != "success" || searchResult.data == null || searchResult.data.Count == 0)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Tidak Ditemukan", "Belum ada Hasil Stok Opname untuk perintah ini.", "OK");
                });
                return;
            }

            // Ambil number dari hasil pertama (OPR.xxxxx)
            string resultNumber = searchResult.data[0].number;

            // Step 2: Hit stokopname-result/detail.php?number={resultNumber}
            string apiUrl = $"{App.API_HOST}stokopname-result/detail.php?number={Uri.EscapeDataString(resultNumber)}";
            var response = await client.GetAsync(apiUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                try
                {
                    var result = JsonConvert.DeserializeObject<StokOpnameDetailResponse>(responseContent);
                    
                    if (result?.status == "success" && result.data != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            var data = result.data;
                            LblNumber.Text = data.number ?? "-";
                            LblOrderNumber.Text = data.order?.number ?? "-";
                            LblTransDate.Text = data.transDate ?? "-";
                            LblDescription.Text = data.description ?? "-";
                            LblTotalItems.Text = $"{(data.detailItem?.Count ?? 0)} ITEMS";

                            _allItems = data.detailItem ?? new List<StokOpnameDetailItem>();
                            CV_Items.ItemsSource = _allItems;
                        });
                    }
                    else
                    {
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            await DisplayAlert("Respons Gagal", "Pesan: " + (result?.message ?? "Null") + "\n\nRaw: " + responseContent, "OK");
                        });
                    }
                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await DisplayAlert("Error Parsing", "Gagal membaca JSON:\n" + ex.Message + "\n\nRaw: " + responseContent, "OK");
                    });
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("HTTP Error", $"URL: {apiUrl}\nStatus Code: {response.StatusCode}\nError: {errorContent}", "OK");
                });
            }
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Exception", ex.Message, "OK");
            });
        }
        finally
        {
            await delayTask;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OverlayLoading.IsVisible = false;
            });
        }
    }

    private async void BtnBack_Tapped(object sender, TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void BtnLihatSemua_Clicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is StokOpnameDetailItem item)
        {
            item.ShowAllSerialNumbers = true;
        }
    }

    private async void BtnSearch_Tapped(object sender, TappedEventArgs e)
    {
        if (_allItems == null || _allItems.Count == 0) return;

        string searchResult = await DisplayPromptAsync("Pencarian", "Masukkan ID Produk atau Nama", "Cari", "Tampilkan Semua");
        
        if (searchResult == null) 
        {
            // Null means user pressed Tampilkan Semua / Batal
            CV_Items.ItemsSource = _allItems;
            LblTotalItems.Text = $"{_allItems.Count} ITEMS";
            return;
        }

        if (string.IsNullOrWhiteSpace(searchResult))
        {
            CV_Items.ItemsSource = _allItems;
            LblTotalItems.Text = $"{_allItems.Count} ITEMS";
        }
        else
        {
            var filtered = _allItems.Where(x => 
                (x.ItemName != null && x.ItemName.Contains(searchResult, StringComparison.OrdinalIgnoreCase)) ||
                (x.ItemNo != null && x.ItemNo.Contains(searchResult, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            CV_Items.ItemsSource = filtered;
            LblTotalItems.Text = $"{filtered.Count} ITEMS";
        }
    }

    private async void BtnExportPdf_Clicked(object sender, EventArgs e)
    {
        BtnExportPdf.IsEnabled = false;
        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

            string apiUrl = $"{App.API_HOST}stokopname-order/detail.php?number={Uri.EscapeDataString(TransNumber)}";
            var response = await client.GetAsync(apiUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<SODetailResponse>(responseContent);
                
                // Fetch company profile
                string companyApiUrl = $"{App.API_HOST}profile/company.php";
                var companyResponse = await client.GetAsync(companyApiUrl);
                SOCompanyProfileData companyProfile = null;
                if (companyResponse.IsSuccessStatusCode)
                {
                    var companyContent = await companyResponse.Content.ReadAsStringAsync();
                    try {
                        var cResult = JsonConvert.DeserializeObject<SOCompanyProfileResponse>(companyContent);
                        if (cResult?.data != null) companyProfile = cResult.data;
                    } catch {}
                }
                
                if (result?.status == "success" && result.data != null)
                {
#if ANDROID
                    byte[] pdfBytes = await Task.Run(() => BuildReportPdfAndroid(result.data, companyProfile));
                    string filePath = Path.Combine(FileSystem.CacheDirectory, $"SO_REPORT_{TransNumber}.pdf");
                    File.WriteAllBytes(filePath, pdfBytes);

                    await Share.RequestAsync(new ShareFileRequest
                    {
                        Title = "Simpan / Bagikan Laporan Stok Opname PDF",
                        File = new ShareFile(filePath)
                    });
#else
                    await DisplayAlert("Tidak Didukung", "Pembuatan PDF laporan hanya didukung di Android.", "OK");
#endif
                }
                else
                {
                    await DisplayAlertAsync("Gagal", "Gagal memuat data detail stok opname order.", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"Gagal membuat PDF: {ex.Message}", "OK");
        }
        finally
        {
            BtnExportPdf.IsEnabled = true;
        }
    }

#if ANDROID
    private byte[] BuildReportPdfAndroid(SODetailData data, SOCompanyProfileData company)
    {
        const int pageW = 595;
        const int pageH = 842;
        const float margin = 40f;
        float contentTop = margin;
        float contentBottom = pageH - margin;

        var cBlack = Android.Graphics.Color.Argb(255, 0, 0, 0);
        var cGrey = Android.Graphics.Color.Argb(255, 119, 119, 119);
        var cLine = Android.Graphics.Color.Argb(255, 0, 0, 0);

        var pHeader = new Android.Graphics.Paint { Color = cBlack, TextSize = 14, FakeBoldText = true, AntiAlias = true };
        var pTitle = new Android.Graphics.Paint { Color = cBlack, TextSize = 12, FakeBoldText = true, AntiAlias = true };
        var pLabel = new Android.Graphics.Paint { Color = cBlack, TextSize = 10, AntiAlias = true };
        var pValue = new Android.Graphics.Paint { Color = cBlack, TextSize = 10, AntiAlias = true };
        var pValueBold = new Android.Graphics.Paint { Color = cBlack, TextSize = 10, FakeBoldText = true, AntiAlias = true };
        var pTableHead = new Android.Graphics.Paint { Color = cBlack, TextSize = 10, FakeBoldText = true, AntiAlias = true };
        var pTableBody = new Android.Graphics.Paint { Color = cBlack, TextSize = 10, AntiAlias = true };
        var pLinePaint = new Android.Graphics.Paint { Color = cLine, StrokeWidth = 0.5f, AntiAlias = true };
        var pBorderPaint = new Android.Graphics.Paint { Color = cLine, StrokeWidth = 0.5f, AntiAlias = true };
        pBorderPaint.SetStyle(Android.Graphics.Paint.Style.Stroke);
        
        var pCompany = new Android.Graphics.Paint { Color = cBlack, TextSize = 18, FakeBoldText = true, AntiAlias = true };
        var pCompanyInfo = new Android.Graphics.Paint { Color = cGrey, TextSize = 10, AntiAlias = true };

        float xLeft = margin;
        float xRightTotal = pageW - margin;

        float TextH(Android.Graphics.Paint p) => p.Descent() - p.Ascent();
        float Baseline(float top, Android.Graphics.Paint p) => top - p.Ascent();

        var blocks = new List<(float h, Action<Android.Graphics.Canvas, float> draw)>();
        void AddBlock(float height, Action<Android.Graphics.Canvas, float> draw) => blocks.Add((height, draw));

        void AddCenter(string text, Android.Graphics.Paint p, float gap)
        {
            if (string.IsNullOrEmpty(text)) return;
            float h = TextH(p) + gap;
            AddBlock(h, (canvas, top) =>
            {
                float w = p.MeasureText(text);
                canvas.DrawText(text, (pageW - w) / 2f, Baseline(top, p), p);
            });
        }

        void AddSpacer(float height) => AddBlock(height, (canvas, top) => { });

        List<string> WrapText(string text, Android.Graphics.Paint p, float maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            var words = text.Split(' ');
            string currentLine = "";
            foreach (var word in words)
            {
                string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                if (p.MeasureText(testLine) <= maxWidth)
                {
                    currentLine = testLine;
                }
                else
                {
                    if (!string.IsNullOrEmpty(currentLine)) lines.Add(currentLine);
                    currentLine = word;
                }
            }
            if (!string.IsNullOrEmpty(currentLine)) lines.Add(currentLine);
            return lines;
        }

        // Headers
        if (company != null)
        {
            AddCenter(company.name, pCompany, 4);
            var distParts = new[] { company.district?.district, company.district?.province, company.district?.country }.Where(s => !string.IsNullOrWhiteSpace(s));
            string distStr = string.Join(", ", distParts);
            string addressFull = string.IsNullOrWhiteSpace(company.address) ? distStr : (string.IsNullOrWhiteSpace(distStr) ? company.address : $"{company.address}, {distStr}");
            AddCenter(addressFull, pCompanyInfo, 2);
            
            var contactParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(company.phone)) contactParts.Add($"Telp: {company.phone}");
            if (!string.IsNullOrWhiteSpace(company.email)) contactParts.Add($"Email: {company.email}");
            if (contactParts.Count > 0) AddCenter(string.Join(" • ", contactParts), pCompanyInfo, 6);
        }
        else
        {
            AddCenter("HEADER IDENTITAS PERUSAHAAN", pHeader, 10);
        }
        AddCenter("LAPORAN STOKOPNAME", pTitle, 20);

        // Information Grid
        string cabang = data.branchName ?? "-";
        string gudang = data.warehouse?.name ?? "-";
        string petugas = data.stockOpnameResultList?.FirstOrDefault()?.fullname ?? "-";
        string pj = data.personCharged ?? "-";
        string status = data.statusName ?? "-";
        string tglSpk = data.transDateView ?? "-";
        string tglMulai = data.startDate ?? "-";
        string kategori = data.itemCategoryList?.FirstOrDefault()?.name ?? "-";
        string catatan = data.description ?? "-";
        string noSpk = data.number ?? "-";

        float col1 = xLeft;
        float col2 = xLeft + 90;
        float col3 = pageW / 2 + 20;
        float col4 = col3 + 90;

        void AddInfoRow(string lbl1, string val1, string lbl2, string val2)
        {
            float h = Math.Max(TextH(pLabel), TextH(pValue)) + 8;
            AddBlock(h, (canvas, top) =>
            {
                float bl = Baseline(top, pLabel);
                canvas.DrawText(lbl1, col1, bl, pLabel);
                canvas.DrawText($": {val1}", col2, bl, pValue);
                if (lbl2 != null)
                {
                    canvas.DrawText(lbl2, col3, bl, pLabel);
                    canvas.DrawText($": {val2}", col4, bl, pValue);
                }
            });
        }

        AddInfoRow("Cabang", cabang, "Tanggal SPK", tglSpk);
        AddInfoRow("Gudang", gudang, "Tanggal Mulai", tglMulai);
        AddInfoRow("Petugas", petugas, "Kategori Barang", kategori);
        AddInfoRow("Penanggung Jawab", pj, "Catatan", catatan);
        AddInfoRow("Status", status, "No. SPK", noSpk);

        AddSpacer(20);

        // Table Title
        string oprNo = data.stockOpnameResultList?.FirstOrDefault()?.number ?? "-";
        string tableTitle = $"RIWAYAT HITUNG DETAIL BARANG ({oprNo})";
        AddBlock(TextH(pTitle) + 10, (canvas, top) => canvas.DrawText(tableTitle, xLeft, Baseline(top, pTitle), pTitle));

        // Table config
        float[] cols = {
            xLeft,
            xLeft + 30,
            xLeft + 110,
            xRightTotal - 170, // Satuan
            xRightTotal - 120, // Stok Sistem
            xRightTotal - 60,  // Stok Hitung
            xRightTotal
        };
        
        void AddTableRow(string[] texts, Android.Graphics.Paint p, bool isHeader = false)
        {
            float padding = 10f;
            float maxH = 0;
            var wrappedTexts = new List<List<string>>();
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null) { wrappedTexts.Add(new List<string>()); continue; }
                float colWidth = cols[i+1] - cols[i] - 10f;
                var lines = WrapText(texts[i], p, colWidth);
                wrappedTexts.Add(lines);
                float h = lines.Count * TextH(p);
                if (h > maxH) maxH = h;
            }
            float rowHeight = Math.Max(25f, maxH + padding);

            AddBlock(rowHeight, (canvas, top) =>
            {
                canvas.DrawRect(cols[0], top, cols[6], top + rowHeight, pBorderPaint);
                for (int i = 1; i < cols.Length - 1; i++)
                    canvas.DrawLine(cols[i], top, cols[i], top + rowHeight, pLinePaint);

                for (int i = 0; i < texts.Length; i++)
                {
                    var lines = wrappedTexts[i];
                    if (lines.Count == 0) continue;
                    
                    float colWidth = cols[i+1] - cols[i];
                    float startY = top + (rowHeight - (lines.Count * TextH(p))) / 2f - p.Ascent();
                    
                    for(int j = 0; j < lines.Count; j++)
                    {
                        float textW = p.MeasureText(lines[j]);
                        float colCenter = cols[i] + colWidth / 2f;
                        
                        if (isHeader || i >= 3)
                            canvas.DrawText(lines[j], colCenter - textW / 2f, startY + (j * TextH(p)), p);
                        else
                            canvas.DrawText(lines[j], cols[i] + 5, startY + (j * TextH(p)), p);
                    }
                }
            });
        }

        // Headers
        AddTableRow(new[] { "No", "Kode Barang", "Nama Barang", "Satuan", "Stok Sistem", "Stok Hitung" }, pTableHead, true);

        // Body
        int idx = 1;
        double sumSistem = 0;
        double sumHitung = 0;
        if (data.detailItem != null)
        {
            foreach (var item in data.detailItem)
            {
                sumSistem += item.quantity;
                sumHitung += item.quantityResult ?? 0;
                AddTableRow(new[] { idx.ToString(), item.item?.no, item.item?.name, item.itemUnit?.name, item.quantity.ToString(), item.quantityResult?.ToString() }, pTableBody);
                idx++;
            }
        }

        // Footer Total
        AddBlock(25f, (canvas, top) =>
        {
            canvas.DrawRect(cols[0], top, cols[6], top + 25f, pBorderPaint);
            for (int i = 4; i < cols.Length - 1; i++) canvas.DrawLine(cols[i], top, cols[i], top + 25f, pLinePaint);

            float bl = top + 25f / 2f - (pTableHead.Ascent() + pTableHead.Descent()) / 2f;
            float textW = pTableHead.MeasureText("Total");
            canvas.DrawText("Total", cols[0] + (cols[4] - cols[0]) / 2f - textW / 2f, bl, pTableHead);

            string sSistem = sumSistem.ToString();
            canvas.DrawText(sSistem, cols[4] + (cols[5] - cols[4]) / 2f - pTableHead.MeasureText(sSistem) / 2f, bl, pTableHead);

            string sHitung = sumHitung.ToString();
            canvas.DrawText(sHitung, cols[5] + (cols[6] - cols[5]) / 2f - pTableHead.MeasureText(sHitung) / 2f, bl, pTableHead);
        });

        AddSpacer(30);

        // Adjustment Section
        AddBlock(TextH(pTitle) + 15, (canvas, top) => canvas.DrawText("INFORMASI PERHITUNGAN DAN PENYESUAIAN STOK", xLeft, Baseline(top, pTitle), pTitle));

        string adjNo = data.itemAdjustment?.number ?? "-";
        string adjDate = data.itemAdjustment?.transDateView ?? "-";
        string adjCount = data.itemAdjustment != null ? data.itemAdjustment.totalItem.ToString() : "-";
        string hitungCount = data.stockOpnameResultList?.FirstOrDefault()?.countDetail.ToString() ?? "-";

        void AddAdjRow(string lbl, string val)
        {
            float h = Math.Max(TextH(pLabel), TextH(pValueBold)) + 8;
            AddBlock(h, (canvas, top) =>
            {
                float bl = Baseline(top, pLabel);
                canvas.DrawText(lbl, xLeft, bl, pLabel);
                canvas.DrawText(val, xLeft + 200, bl, pValueBold);
            });
        }

        AddAdjRow("Nomor Penyesuaian Sistem", adjNo);
        AddAdjRow("Tanggal Penyesuaian Sistem", adjDate);
        AddAdjRow("Total (Rencana SO)", sumSistem.ToString());
        AddAdjRow("Total (Perhitungan SO)", hitungCount);
        AddAdjRow("Jumlah Penyesuaian Sistem", adjCount);

        // Generate PDF
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

// Models
public class StokOpnameResultListResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public List<StokOpnameResultListItem> data { get; set; }
}

public class StokOpnameResultListItem
{
    public string number { get; set; }
    public string transDate { get; set; }
    public string description { get; set; }
    public int id { get; set; }
}

public class SOCompanyProfileResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public SOCompanyProfileData data { get; set; }
}

public class SOCompanyProfileData
{
    public string email { get; set; }
    public string address { get; set; }
    public string phone { get; set; }
    public SOCompanyDistrict district { get; set; }
    public string name { get; set; }
}

public class SOCompanyDistrict
{
    public string country { get; set; }
    public string province { get; set; }
    public string district { get; set; }
}

public class StokOpnameDetailResponse
{
    public string status { get; set; }
    public string message { get; set; }
    public StokOpnameDetail data { get; set; }
}

public class StokOpnameDetail
{
    public string number { get; set; }
    public int id { get; set; }
    public StokOpnameOrder order { get; set; }
    public List<StokOpnameDetailItem> detailItem { get; set; }
    public string description { get; set; }
    public string transDate { get; set; }
}

public class StokOpnameOrder
{
    public string number { get; set; }
    public int id { get; set; }
    public string startDate { get; set; }
    public string status { get; set; }
}

public class StokOpnameItem
{
    public string name { get; set; }
    public string no { get; set; }
    public StokOpnameUnit unit1 { get; set; }
}

public class StokOpnameUnit
{
    public string name { get; set; }
}

public class StokOpnameDetailSerialNumber
{
    public double quantity { get; set; }
    public StokOpnameSerialNumber serialNumber { get; set; }
}

public class StokOpnameSerialNumber
{
    public string number { get; set; }
    public string updateStockDate { get; set; }
}

public class StokOpnameDetailItem : INotifyPropertyChanged
{
    public StokOpnameItem item { get; set; }
    public double quantity { get; set; }
    public List<StokOpnameDetailSerialNumber> detailSerialNumber { get; set; }

    public string ItemName => item?.name ?? "-";
    public string ItemNo => $"ID: {item?.no ?? "-"}";
    public string UnitName => item?.unit1?.name ?? "-";
    public Color QuantityColor => quantity == 0 ? Colors.Red : Colors.DarkCyan;
    public bool HasSerialNumbers => detailSerialNumber != null && detailSerialNumber.Count > 0;
    public bool HasNoSerialNumbers => !HasSerialNumbers;

    public List<StokOpnameDetailSerialNumber> DisplaySerialNumbers
    {
        get
        {
            if (detailSerialNumber == null) return new List<StokOpnameDetailSerialNumber>();
            return ShowAllSerialNumbers ? detailSerialNumber : detailSerialNumber.Take(20).ToList();
        }
    }

    public bool ShowMoreButton => detailSerialNumber != null && detailSerialNumber.Count > 20 && !ShowAllSerialNumbers;

    private bool _showAllSerialNumbers;
    public bool ShowAllSerialNumbers
    {
        get => _showAllSerialNumbers;
        set
        {
            if (_showAllSerialNumbers != value)
            {
                _showAllSerialNumbers = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowAllSerialNumbers)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplaySerialNumbers)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowMoreButton)));
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}