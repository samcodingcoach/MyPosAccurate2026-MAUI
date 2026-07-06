using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;
using System.Linq;

#if ANDROID
using Android.Bluetooth;
using Java.Util;
#endif

namespace MyPosAccurate2026.Sales;

public partial class Print : ContentPage
{
    private static readonly CultureInfo IdCulture = new CultureInfo("id-ID");

    private string _receiptNumber = "";   // nomor struk (110103.2026.06.xxxxx)
    private string _invoiceNumber = "";   // nomor faktur SI (fallback untuk detail-invoice)
    private double _nominalBayar = 0;     // khusus tunai: nominal uang yang dibayarkan konsumen

    private DetailReceiptData _receipt;
    private DetailInvoiceData _invoice;
    private CompanyProfileData _company;
    private string _invoiceNoStr;

#if ANDROID
    private static readonly UUID SPP_UUID = UUID.FromString("00001101-0000-1000-8000-00805f9b34fb");
#endif

    public Print()
    {
        InitializeComponent();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // Dipanggil dari alur pembayaran (Tunai/QRIS/VA) setelah save-receipt sukses.
    // nominalBayar hanya relevan untuk Tunai (uang yang diserahkan konsumen); 0 untuk QRIS/VA.
    public Print(string receiptNumber, string invoiceNumber, double nominalBayar = 0) : this()
    {
        _receiptNumber = receiptNumber ?? "";
        _invoiceNumber = invoiceNumber ?? "";
        _nominalBayar = nominalBayar;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadStrukAsync();
    }

    private static string FormatRupiah(double nilai) => $"Rp {nilai.ToString("N0", IdCulture)}";

    private async Task LoadStrukAsync()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StrukLoading.IsRunning = true;
            StrukLoading.IsVisible = true;
        });

        try
        {
            string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();

            DetailReceiptData receipt = null;

            // 1. Data pembayaran (struk) — butuh nomor struk
            if (!string.IsNullOrWhiteSpace(_receiptNumber))
                receipt = await FetchReceiptAsync(cleanToken, _receiptNumber);

            // Nomor faktur: utamakan dari respon struk, fallback ke yang dibawa konstruktor
            string invoiceNo = receipt?.detailInvoice?.FirstOrDefault()?.invoice?.number;
            if (string.IsNullOrWhiteSpace(invoiceNo))
                invoiceNo = _invoiceNumber;

            // 2. Detail barang (faktur)
            DetailInvoiceData invoice = null;
            if (!string.IsNullOrWhiteSpace(invoiceNo))
                invoice = await FetchInvoiceAsync(cleanToken, invoiceNo);

            // 3. Data Company Profile
            var companyData = await FetchCompanyProfileAsync(cleanToken);

            _receipt = receipt;
            _invoice = invoice;
            _invoiceNoStr = invoiceNo;
            _company = companyData;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                TampilkanStruk(receipt, invoice, invoiceNo, companyData);
                StrukLoading.IsRunning = false;
                StrukLoading.IsVisible = false;
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StrukLoading.IsRunning = false;
                StrukLoading.IsVisible = false;
            });
            await DisplayAlertAsync("Gagal Memuat Struk", $"Terjadi kesalahan: {ex.Message}", "OK");
        }
    }

    private async Task<DetailReceiptData> FetchReceiptAsync(string token, string number)
    {
        string url = $"{App.API_HOST}penjualan/detail-receipt.php?number={Uri.EscapeDataString(number)}";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(url);
        string responseContent = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(responseContent) || responseContent.TrimStart().StartsWith("<"))
            return null;

        var result = JsonConvert.DeserializeObject<DetailReceiptResponse>(responseContent);
        return result?.status == "success" ? result.data : null;
    }

    private async Task<DetailInvoiceData> FetchInvoiceAsync(string token, string number)
    {
        string url = $"{App.API_HOST}penjualan/detail-invoice.php?number={Uri.EscapeDataString(number)}";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(url);
        string responseContent = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(responseContent) || responseContent.TrimStart().StartsWith("<"))
            return null;

        var result = JsonConvert.DeserializeObject<DetailInvoiceResponse>(responseContent);
        return result?.data;
    }

    private async Task<CompanyProfileData> FetchCompanyProfileAsync(string token)
    {
        string url = $"{App.API_HOST}profile/company.php";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var response = await client.GetAsync(url);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(responseContent) || responseContent.TrimStart().StartsWith("<"))
                return null;

            var result = JsonConvert.DeserializeObject<CompanyProfileResponse>(responseContent);
            return result?.data;
        }
        catch
        {
            return null;
        }
    }

    private void TampilkanStruk(DetailReceiptData receipt, DetailInvoiceData invoice, string invoiceNo, CompanyProfileData companyData)
    {
        // ===== Info transaksi =====
        LabelNoStruk.Text = string.IsNullOrWhiteSpace(_receiptNumber) ? "-" : _receiptNumber;
        LabelNoFaktur.Text = string.IsNullOrWhiteSpace(invoiceNo) ? "-" : invoiceNo;
        LabelTanggal.Text = receipt?.transDate ?? invoice?.transDate ?? "-";
        LabelKasir.Text = string.IsNullOrWhiteSpace(receipt?.charField2) ? "-" : receipt.charField2;

        if (receipt?.customer != null)
            LabelKonsumen.Text = $"{receipt.customer.customerNo} - {receipt.customer.name}";
        else
            LabelKonsumen.Text = "-";

        // Sales, pengiriman, alamat (dari detail-invoice)
        string sales = invoice?.masterSalesmanName;
        RowSales.IsVisible = !string.IsNullOrWhiteSpace(sales);
        LabelSales.Text = sales ?? "-";

        string pengiriman = invoice?.shipment?.name;
        RowPengiriman.IsVisible = !string.IsNullOrWhiteSpace(pengiriman);
        LabelPengiriman.Text = pengiriman ?? "-";

        string alamat = invoice?.toAddress;
        if (!string.IsNullOrWhiteSpace(alamat))
        {
            var gpsMatch = System.Text.RegularExpressions.Regex.Match(alamat, @"\[GPS:(.+?)\]");
            if (gpsMatch.Success)
            {
                string gpsValue = gpsMatch.Groups[1].Value.Trim();
                GpsQrCode.Value = $"https://maps.google.com/?q={gpsValue}";
                GpsQrCode.IsVisible = true;
                alamat = alamat.Replace(gpsMatch.Value, "");
            }
            else
            {
                GpsQrCode.IsVisible = false;
            }

            alamat = System.Text.RegularExpressions.Regex.Replace(alamat, @"\b\d{5}\b", "");
            alamat = System.Text.RegularExpressions.Regex.Replace(alamat, @"(?i)\bIndonesia\b", "");
            
            alamat = System.Text.RegularExpressions.Regex.Replace(alamat, @"\s+", " ");
            alamat = System.Text.RegularExpressions.Regex.Replace(alamat, @"\s*,\s*", ", ");
            alamat = alamat.Trim().TrimEnd(',').Trim();
        }
        else
        {
            GpsQrCode.IsVisible = false;
        }

        RowAlamat.IsVisible = !string.IsNullOrWhiteSpace(alamat) || GpsQrCode.IsVisible;
        LabelAlamat.Text = alamat ?? "";

        // ===== Daftar item =====
        var items = (invoice?.detailItem ?? new List<InvItem>())
            .Select(d => new StrukItem
            {
                NamaBarang = d.item?.name,
                quantity = d.quantity,
                unitPrice = d.unitPrice,
                satuan = d.itemUnit?.name
            })
            .ToList();
        ListItemContainer.BindingContext = items;

        // ===== Subtotal & diskon faktur =====
        LabelSubtotal.Text = FormatRupiah(invoice?.subTotal ?? 0);

        double diskonFaktur = invoice?.cashDiscount ?? 0;
        RowDiskonFaktur.IsVisible = diskonFaktur > 0;
        LabelDiskonFaktur.Text = $"- {FormatRupiah(diskonFaktur)}";

        // ===== Biaya-biaya (detailExpense) =====
        var expenses = (invoice?.detailExpense ?? new List<InvExpense>())
            .Select(x => new StrukExpense { detailName = x.detailName, expenseAmount = x.expenseAmount })
            .ToList();
        bool adaBiaya = expenses.Count > 0;
        LabelBiayaHeader.IsVisible = adaBiaya;
        ListExpenseContainer.IsVisible = adaBiaya;
        ListExpenseContainer.BindingContext = expenses;

        // ===== Pajak & total =====
        LabelPajak.Text = FormatRupiah(invoice?.tax1AmountBase ?? 0);
        double total = invoice?.totalAmount ?? receipt?.totalPayment ?? 0;
        LabelTotal.Text = FormatRupiah(total);

        // ===== Metode =====
        string metode = invoice?.receiptHistory?.FirstOrDefault()?.historyPaymentName;
        if (string.IsNullOrWhiteSpace(metode))
            metode = LabelMetodeFromCode(receipt?.paymentMethod);
        LabelMetode.Text = metode;

        // ===== Khusus Tunai: nominal dibayar & kembalian =====
        bool isTunai = string.Equals(receipt?.paymentMethod, "CASH_OTHER", StringComparison.OrdinalIgnoreCase);
        BoxTunai.IsVisible = isTunai;
        if (isTunai)
        {
            // Dasar tagihan = nilai yang diterima (pembulatan) bila ada, jika tidak pakai total faktur
            double tagihan = receipt?.totalPayment > 0 ? receipt.totalPayment : total;

            // Nominal dibayar: utamakan yang dibawa dari halaman pembayaran;
            // fallback ke kembalian tersimpan (numericField1) + tagihan
            double bayar = _nominalBayar > 0 ? _nominalBayar : tagihan + (receipt?.numericField1 ?? 0);

            double kembalian = bayar - tagihan;
            if (kembalian < 0) kembalian = 0;

            LabelBayar.Text = FormatRupiah(bayar);
            LabelKembalian.Text = FormatRupiah(kembalian);
        }

        // ===== Catatan =====
        string catatan = receipt?.description;
        RowCatatan.IsVisible = !string.IsNullOrWhiteSpace(catatan);
        LabelCatatan.Text = catatan ?? "";

        // ===== Footer Company & Header Company =====
        if (companyData != null)
        {
            LabelHeaderCompanyName.Text = companyData.name ?? "-";
            LabelCompanyName.Text = companyData.name ?? "-";
            
            string addressCity = "";
            if (!string.IsNullOrWhiteSpace(companyData.address)) addressCity += companyData.address;
            if (!string.IsNullOrWhiteSpace(companyData.city)) 
            {
                if (!string.IsNullOrEmpty(addressCity)) addressCity += ", ";
                addressCity += companyData.city;
            }
            LabelCompanyAddressCity.Text = string.IsNullOrEmpty(addressCity) ? "-" : $"📍 {addressCity}";
            
            string contact = "";
            if (!string.IsNullOrWhiteSpace(companyData.phone)) contact += $"📞 {companyData.phone}";
            if (!string.IsNullOrWhiteSpace(companyData.email)) 
            {
                if (!string.IsNullOrEmpty(contact)) contact += "   ";
                contact += $"✉ {companyData.email}";
            }
            LabelCompanyContact.Text = string.IsNullOrEmpty(contact) ? "-" : contact;
        }
        
        LabelPrintDate.Text = $"Dicetak pada: {DateTime.Now.ToString("dd MMM yyyy HH:mm:ss", IdCulture)}";
    }

    private static string LabelMetodeFromCode(string code)
    {
        switch ((code ?? "").ToUpperInvariant())
        {
            case "CASH_OTHER": return "Tunai";
            case "QRIS": return "QRIS";
            case "BANK_TRANSFER": return "Transfer Bank / VA";
            default: return string.IsNullOrWhiteSpace(code) ? "-" : code;
        }
    }

    // ===== Tombol bawah =====
    

    private string AlignRight(string label, string value, int totalLength)
    {
        if (label == null) label = "";
        if (value == null) value = "";
        
        int spacing = totalLength - (label.Length + value.Length);
        if (spacing < 1) spacing = 1;
        return label + new string(' ', spacing) + value;
    }

    private string CenterText(string text, int totalLength)
    {
        if (text == null) text = "";
        if (text.Length >= totalLength) return text;
        int padding = (totalLength - text.Length) / 2;
        return new string(' ', padding) + text;
    }

#if ANDROID
    private async Task ExecutePrint(BluetoothDevice device, int paperSize)
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            string line = new string('-', paperSize) + "\n";
            string lineEq = new string('=', paperSize) + "\n";

            // ESC POS Init
            sb.Append("\x1B\x40");

            // Header
            sb.Append("\x1B\x61\x01"); // Center align
            sb.Append("\x1B\x21\x08"); // Bold
            sb.Append("STRUK PEMBAYARAN\n");
            sb.Append("\x1B\x21\x00"); // Normal
            sb.Append($"{_company?.name ?? "POS ACCURATE"}\n");
            sb.Append("\x1B\x61\x00"); // Left align
            sb.Append(line);

            // Info transaksi
            string tgl = _receipt?.transDate ?? _invoice?.transDate ?? "-";
            string kasir = string.IsNullOrWhiteSpace(_receipt?.charField2) ? "-" : _receipt.charField2;
            string konsumen = _receipt?.customer != null ? $"{_receipt.customer.customerNo} - {_receipt.customer.name}" : "-";
            
            sb.Append(AlignRight("No. Struk", string.IsNullOrWhiteSpace(_receiptNumber) ? "-" : _receiptNumber, paperSize) + "\n");
            sb.Append(AlignRight("No. Faktur", string.IsNullOrWhiteSpace(_invoiceNoStr) ? "-" : _invoiceNoStr, paperSize) + "\n");
            sb.Append(AlignRight("Tanggal", tgl, paperSize) + "\n");
            sb.Append(AlignRight("Kasir", kasir, paperSize) + "\n");
            sb.Append(AlignRight("Konsumen", konsumen, paperSize) + "\n");
            
            string sales = _invoice?.masterSalesmanName;
            if (!string.IsNullOrWhiteSpace(sales)) sb.Append(AlignRight("Sales", sales, paperSize) + "\n");
            
            string pengiriman = _invoice?.shipment?.name;
            if (!string.IsNullOrWhiteSpace(pengiriman)) sb.Append(AlignRight("Pengiriman", pengiriman, paperSize) + "\n");
            
            string gpsQrUrl = null;
            string alamat = _invoice?.toAddress;
            if (!string.IsNullOrWhiteSpace(alamat))
            {
                var gpsMatch = System.Text.RegularExpressions.Regex.Match(alamat, @"\[GPS:(.+?)\]");
                if (gpsMatch.Success)
                {
                    gpsQrUrl = $"https://maps.google.com/?q={gpsMatch.Groups[1].Value.Trim()}";
                    alamat = alamat.Replace(gpsMatch.Value, "");
                }

                alamat = System.Text.RegularExpressions.Regex.Replace(alamat, @"\b\d{5}\b", "");
                alamat = System.Text.RegularExpressions.Regex.Replace(alamat, @"(?i)\bIndonesia\b", "");
                alamat = System.Text.RegularExpressions.Regex.Replace(alamat, @"\s+", " ");
                alamat = System.Text.RegularExpressions.Regex.Replace(alamat, @"\s*,\s*", ", ");
                alamat = alamat.Trim().TrimEnd(',').Trim();

                if (!string.IsNullOrWhiteSpace(alamat))
                {
                    sb.Append($"Alamat:\n{alamat}\n");
                }
            }

            sb.Append(line);

            // Items header
            sb.Append(AlignRight("ITEM", "TOTAL", paperSize) + "\n");

            // Items
            var items = _invoice?.detailItem ?? new List<InvItem>();
            foreach (var itm in items)
            {
                sb.Append($"{itm.item?.name}\n");
                string qtyPrice = $"{itm.quantity.ToString("0.##", IdCulture)} {itm.itemUnit?.name} x {FormatRupiah(itm.unitPrice)}";
                string totalLn = FormatRupiah(itm.quantity * itm.unitPrice);
                sb.Append(AlignRight(qtyPrice, totalLn, paperSize) + "\n");
            }

            sb.Append(line);

            // Ringkasan
            sb.Append(AlignRight("Subtotal", FormatRupiah(_invoice?.subTotal ?? 0), paperSize) + "\n");
            
            double diskonFaktur = _invoice?.cashDiscount ?? 0;
            if (diskonFaktur > 0)
                sb.Append(AlignRight("Diskon Faktur", $"- {FormatRupiah(diskonFaktur)}", paperSize) + "\n");

            var expenses = _invoice?.detailExpense ?? new List<InvExpense>();
            if (expenses.Count > 0)
            {
                sb.Append("Biaya-biaya\n");
                foreach (var exp in expenses)
                {
                    sb.Append(AlignRight($"  {exp.detailName}", FormatRupiah(exp.expenseAmount), paperSize) + "\n");
                }
            }

            sb.Append(AlignRight("Total Pajak (PPN)", FormatRupiah(_invoice?.tax1AmountBase ?? 0), paperSize) + "\n");
            
            sb.Append(lineEq);
            
            double totalAll = _invoice?.totalAmount ?? _receipt?.totalPayment ?? 0;
            sb.Append("\x1B\x21\x08"); // Bold
            sb.Append(AlignRight("TOTAL", FormatRupiah(totalAll), paperSize) + "\n");
            sb.Append("\x1B\x21\x00"); // Normal
            
            sb.Append(lineEq);

            // Tunai Dibayar & Kembalian
            bool isTunai = string.Equals(_receipt?.paymentMethod, "CASH_OTHER", StringComparison.OrdinalIgnoreCase);
            if (isTunai)
            {
                double tagihan = _receipt?.totalPayment > 0 ? _receipt.totalPayment : totalAll;
                double bayar = _nominalBayar > 0 ? _nominalBayar : tagihan + (_receipt?.numericField1 ?? 0);
                double kembalian = bayar - tagihan;
                if (kembalian < 0) kembalian = 0;

                sb.Append(AlignRight("Tunai Dibayar", FormatRupiah(bayar), paperSize) + "\n");
                sb.Append(AlignRight("Kembalian", FormatRupiah(kembalian), paperSize) + "\n");
            }

            // Metode
            string metode = _invoice?.receiptHistory?.FirstOrDefault()?.historyPaymentName;
            if (string.IsNullOrWhiteSpace(metode))
                metode = LabelMetodeFromCode(_receipt?.paymentMethod);
            sb.Append(AlignRight("Metode", metode, paperSize) + "\n");

            string catatan = _receipt?.description;
            if (!string.IsNullOrWhiteSpace(catatan))
            {
                sb.Append($"Catatan:\n{catatan}\n");
            }

            sb.Append(line);

            // Footer
            sb.Append("\x1B\x61\x01"); // Center align
            sb.Append("Terima kasih telah berbelanja\n\n");
            
            if (_company != null)
            {
                sb.Append($"{_company.name ?? "-"}\n");
                
                string addressCity = "";
                if (!string.IsNullOrWhiteSpace(_company.address)) addressCity += _company.address;
                if (!string.IsNullOrWhiteSpace(_company.city)) 
                {
                    if (!string.IsNullOrEmpty(addressCity)) addressCity += ", ";
                    addressCity += _company.city;
                }
                if (!string.IsNullOrEmpty(addressCity)) sb.Append($"{addressCity}\n");

                string contact = "";
                if (!string.IsNullOrWhiteSpace(_company.phone)) contact += $"{_company.phone}";
                if (!string.IsNullOrWhiteSpace(_company.email)) 
                {
                    if (!string.IsNullOrEmpty(contact)) contact += "  ";
                    contact += $"{_company.email}";
                }
                if (!string.IsNullOrEmpty(contact)) sb.Append($"{contact}\n");
            }

            sb.Append($"Dicetak pada: {DateTime.Now.ToString("dd MMM yyyy HH:mm:ss", IdCulture)}\n");

            string struk = sb.ToString();
            var bufferList = new System.Collections.Generic.List<byte>(Encoding.GetEncoding(437).GetBytes(struk));

            if (!string.IsNullOrEmpty(gpsQrUrl))
            {
                bufferList.AddRange(Encoding.GetEncoding(437).GetBytes("\nLokasi (Scan):\n"));
                
                // ESC/POS QR Code Commands
                int len = gpsQrUrl.Length + 3;
                byte pL = (byte)(len % 256);
                byte pH = (byte)(len / 256);

                // 1. Model 2
                bufferList.AddRange(new byte[] { 29, 40, 107, 4, 0, 49, 65, 50, 0 });
                // 2. Size 6
                bufferList.AddRange(new byte[] { 29, 40, 107, 3, 0, 49, 67, 6 });
                // 3. Error correction L (48)
                bufferList.AddRange(new byte[] { 29, 40, 107, 3, 0, 49, 69, 48 });
                // 4. Store data
                bufferList.AddRange(new byte[] { 29, 40, 107, pL, pH, 49, 80, 48 });
                bufferList.AddRange(Encoding.ASCII.GetBytes(gpsQrUrl));
                // 5. Print QR
                bufferList.AddRange(new byte[] { 29, 40, 107, 3, 0, 49, 81, 48 });
            }

            bufferList.AddRange(Encoding.ASCII.GetBytes("\n\n\n\n"));
            byte[] buffer = bufferList.ToArray();

            await Task.Delay(500); // Stabilkan koneksi
            using (BluetoothSocket bluetoothSocket = device.CreateRfcommSocketToServiceRecord(SPP_UUID))
            {
                bluetoothSocket.Connect();
                for (int i = 0; i < buffer.Length; i += 512)
                {
                    int size = Math.Min(512, buffer.Length - i);
                    bluetoothSocket.OutputStream.Write(buffer, i, size);
                    await Task.Delay(10);
                }
                bluetoothSocket.OutputStream.Flush();
                bluetoothSocket.Close();
                await DisplayAlertAsync("Sukses", "Print berhasil.", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", $"Gagal print: {ex.Message}", "OK");
        }
    }
#endif

   
    

    // Ubah input nomor jadi format internasional Indonesia (62xxxx) tanpa tanda/spasi.
    private static string NormalisasiNomorWa(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string digits = new string(raw.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("62")) return digits;
        if (digits.StartsWith("0")) return "62" + digits.Substring(1);
        if (digits.StartsWith("8")) return "62" + digits;
        return digits;
    }

    private string SusunPesanWa()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"*{_company?.name ?? "Struk Pembayaran"}*");
        sb.AppendLine("Terima kasih telah berbelanja 🙏");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(_invoiceNoStr)) sb.AppendLine($"No. Faktur: {_invoiceNoStr}");
        double total = _invoice?.totalAmount ?? _receipt?.totalPayment ?? 0;
        sb.AppendLine($"Total: {FormatRupiah(total)}");
        sb.AppendLine();
        sb.AppendLine("Struk pembayaran terlampir dalam bentuk PDF.");
        return sb.ToString();
    }

#if ANDROID
    // Kirim PDF langsung ke chat nomor tujuan di WhatsApp (extra "jid" menargetkan nomor tanpa pilih kontak).
    private bool KirimWhatsAppAndroid(string filePath, string nomorWa, string pesan)
    {
        try
        {
            var context = Android.App.Application.Context;
            var javaFile = new Java.IO.File(filePath);
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                context, context.PackageName + ".fileprovider", javaFile);

            var intent = new Android.Content.Intent(Android.Content.Intent.ActionSend);
            intent.SetType("application/pdf");
            intent.PutExtra(Android.Content.Intent.ExtraStream, uri);
            intent.PutExtra(Android.Content.Intent.ExtraText, pesan);
            intent.PutExtra("jid", $"{nomorWa}@s.whatsapp.net"); // arahkan ke nomor tujuan
            intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission);

            // Utamakan WhatsApp reguler, lalu WhatsApp Business
            string paket = ResolveWhatsAppPackage(context);
            if (paket == null)
                return false; // WhatsApp tidak terpasang -> biar caller pakai fallback

            intent.SetPackage(paket);
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveWhatsAppPackage(Android.Content.Context context)
    {
        foreach (var paket in new[] { "com.whatsapp", "com.whatsapp.w4b" })
        {
            try
            {
                context.PackageManager.GetPackageInfo(paket, 0);
                return paket;
            }
            catch (Android.Content.PM.PackageManager.NameNotFoundException)
            {
                // coba paket berikutnya
            }
        }
        return null;
    }

    // ===================== Generate PDF struk (Android native PdfDocument) =====================
    // QuestPDF tidak menyertakan native runtime untuk Android, jadi di Android kita gambar
    // struk langsung ke PdfDocument bawaan Android. Desain mengikuti tampilan kartu di layar.
    private byte[] BuildReceiptPdfAndroid()
    {
        var items = _invoice?.detailItem ?? new List<InvItem>();
        var expenses = _invoice?.detailExpense ?? new List<InvExpense>();
        double subTotal = _invoice?.subTotal ?? 0;
        double diskonFaktur = _invoice?.cashDiscount ?? 0;
        double pajak = _invoice?.tax1AmountBase ?? 0;
        double total = _invoice?.totalAmount ?? _receipt?.totalPayment ?? 0;

        string tgl = _receipt?.transDate ?? _invoice?.transDate ?? "-";
        string kasir = string.IsNullOrWhiteSpace(_receipt?.charField2) ? "-" : _receipt.charField2;
        string konsumen = _receipt?.customer != null
            ? $"{_receipt.customer.customerNo} - {_receipt.customer.name}" : "-";
        string sales = _invoice?.masterSalesmanName;
        string pengiriman = _invoice?.shipment?.name;
        string metode = _invoice?.receiptHistory?.FirstOrDefault()?.historyPaymentName;
        if (string.IsNullOrWhiteSpace(metode))
            metode = LabelMetodeFromCode(_receipt?.paymentMethod);
        string catatan = _receipt?.description;

        bool isTunai = string.Equals(_receipt?.paymentMethod, "CASH_OTHER", StringComparison.OrdinalIgnoreCase);
        double tagihan = _receipt?.totalPayment > 0 ? _receipt.totalPayment : total;
        double bayar = _nominalBayar > 0 ? _nominalBayar : tagihan + (_receipt?.numericField1 ?? 0);
        double kembalian = bayar - tagihan;
        if (kembalian < 0) kembalian = 0;

        const int width = 400;
        const int margin = 26;

        // ===== Palet warna: hitam, grey, dark cyan =====
        var cBlack = Android.Graphics.Color.Argb(255, 26, 26, 26);    // #1A1A1A judul/nilai
        var cValue = Android.Graphics.Color.Argb(255, 51, 51, 51);    // #333 nilai biasa
        var cGrey = Android.Graphics.Color.Argb(255, 119, 119, 119);  // #777 label
        var cMuted = Android.Graphics.Color.Argb(255, 153, 153, 153); // #999 header kolom
        var cCyan = Android.Graphics.Color.Argb(255, 14, 110, 110);   // #0E6E6E aksen
        var cCyanBg = Android.Graphics.Color.Argb(255, 227, 242, 242);// #E3F2F2 band total
        var cDash = Android.Graphics.Color.Argb(255, 207, 207, 207);  // #CFCFCF garis

        // ===== Paints =====
        var pTitle = new Android.Graphics.Paint { Color = cBlack, TextSize = 18, FakeBoldText = true, AntiAlias = true, LetterSpacing = 0.08f };
        var pHeaderCompany = new Android.Graphics.Paint { Color = cGrey, TextSize = 12, AntiAlias = true };
        var pLabel = new Android.Graphics.Paint { Color = cGrey, TextSize = 12, AntiAlias = true };
        var pValue = new Android.Graphics.Paint { Color = cValue, TextSize = 12, AntiAlias = true };
        var pValueB = new Android.Graphics.Paint { Color = cBlack, TextSize = 12, FakeBoldText = true, AntiAlias = true };
        var pColHead = new Android.Graphics.Paint { Color = cMuted, TextSize = 11, FakeBoldText = true, AntiAlias = true, LetterSpacing = 0.06f };
        var pItemName = new Android.Graphics.Paint { Color = cBlack, TextSize = 13, FakeBoldText = true, AntiAlias = true };
        var pQty = new Android.Graphics.Paint { Color = cGrey, TextSize = 11, AntiAlias = true };
        var pLineTotal = new Android.Graphics.Paint { Color = cBlack, TextSize = 12, FakeBoldText = true, AntiAlias = true };
        var pTotalLabel = new Android.Graphics.Paint { Color = cCyan, TextSize = 15, FakeBoldText = true, AntiAlias = true };
        var pTotalVal = new Android.Graphics.Paint { Color = cCyan, TextSize = 22, FakeBoldText = true, AntiAlias = true };
        var pBoxLabel = new Android.Graphics.Paint { Color = cCyan, TextSize = 12, AntiAlias = true };
        var pBoxVal = new Android.Graphics.Paint { Color = cCyan, TextSize = 14, FakeBoldText = true, AntiAlias = true };
        var pThanks = new Android.Graphics.Paint { Color = cGrey, TextSize = 12, AntiAlias = true, TextSkewX = -0.25f };
        var pFootName = new Android.Graphics.Paint { Color = cBlack, TextSize = 12, FakeBoldText = true, AntiAlias = true };
        var pFootInfo = new Android.Graphics.Paint { Color = cGrey, TextSize = 10, AntiAlias = true };
        var pFootDate = new Android.Graphics.Paint { Color = cMuted, TextSize = 9, AntiAlias = true };
        var pBg = new Android.Graphics.Paint { AntiAlias = true };
        var pDash = new Android.Graphics.Paint { Color = cDash, StrokeWidth = 1.2f, AntiAlias = true };
        pDash.SetStyle(Android.Graphics.Paint.Style.Stroke);
        pDash.SetPathEffect(new Android.Graphics.DashPathEffect(new float[] { 4f, 4f }, 0));

        // Baseline agar teks center vertikal di dalam kotak.
        static float CenterBaseline(float top, float h, Android.Graphics.Paint p)
            => top + h / 2f - (p.Ascent() + p.Descent()) / 2f;

        // Fungsi gambar dipakai 2x: measure (canvas null) lalu draw. Mengembalikan tinggi akhir (y).
        float DrawAll(Android.Graphics.Canvas canvas)
        {
            float y = margin + 18;

            void Center(string text, Android.Graphics.Paint p, float gap)
            {
                text ??= "";
                float w = p.MeasureText(text);
                canvas?.DrawText(text, (width - w) / 2f, y, p);
                y += p.TextSize + gap;
            }
            void Row(string label, string value, Android.Graphics.Paint pL, Android.Graphics.Paint pV)
            {
                value ??= "-";
                canvas?.DrawText(label ?? "", margin, y, pL);
                float w = pV.MeasureText(value);
                canvas?.DrawText(value, width - margin - w, y, pV);
                y += Math.Max(pL.TextSize, pV.TextSize) + 7;
            }
            void WrapLeft(string text, Android.Graphics.Paint p, float maxW)
            {
                text ??= "";
                int start = 0;
                while (start < text.Length)
                {
                    int count = p.BreakText(text.Substring(start), true, maxW, null);
                    if (count <= 0) count = text.Length - start;
                    canvas?.DrawText(text.Substring(start, count), margin, y, p);
                    y += p.TextSize + 4;
                    start += count;
                }
            }
            void Dashed(float gapAbove, float gapBelow)
            {
                y += gapAbove;
                if (canvas != null)
                {
                    using var path = new Android.Graphics.Path();
                    path.MoveTo(margin, y);
                    path.LineTo(width - margin, y);
                    canvas.DrawPath(path, pDash);
                }
                y += gapBelow;
            }

            // ===== Judul & company =====
            Center("STRUK PEMBAYARAN", pTitle, 6);
            if (!string.IsNullOrWhiteSpace(_company?.name)) Center(_company.name, pHeaderCompany, 6);

            Dashed(8, 20);

            // ===== Info transaksi =====
            Row("No. Struk", string.IsNullOrWhiteSpace(_receiptNumber) ? "-" : _receiptNumber, pLabel, pValueB);
            Row("No. Faktur", string.IsNullOrWhiteSpace(_invoiceNoStr) ? "-" : _invoiceNoStr, pLabel, pValue);
            Row("Tanggal", tgl, pLabel, pValue);
            Row("Kasir", kasir, pLabel, pValue);
            Row("Konsumen", konsumen, pLabel, pValueB);
            if (!string.IsNullOrWhiteSpace(sales)) Row("Sales", sales, pLabel, pValue);
            if (!string.IsNullOrWhiteSpace(pengiriman)) Row("Pengiriman", pengiriman, pLabel, pValue);

            Dashed(10, 20);

            // ===== Header kolom item =====
            canvas?.DrawText("ITEM", margin, y, pColHead);
            float tw = pColHead.MeasureText("TOTAL");
            canvas?.DrawText("TOTAL", width - margin - tw, y, pColHead);
            y += pColHead.TextSize + 10;

            // ===== Daftar item =====
            foreach (var itm in items)
            {
                float startY = y;
                // total sejajar baris pertama nama
                string totalLn = FormatRupiah(itm.quantity * itm.unitPrice);
                float w = pLineTotal.MeasureText(totalLn);
                canvas?.DrawText(totalLn, width - margin - w, startY, pLineTotal);

                // nama (wrap, sisakan ruang untuk kolom total)
                WrapLeft(itm.item?.name, pItemName, width - 2 * margin - w - 12);

                string qtyPrice = $"{itm.quantity.ToString("0.##", IdCulture)} {itm.itemUnit?.name} x {FormatRupiah(itm.unitPrice)}";
                canvas?.DrawText(qtyPrice, margin, y, pQty);
                y += pQty.TextSize + 12;
            }

            Dashed(2, 20);

            // ===== Ringkasan =====
            Row("Subtotal", FormatRupiah(subTotal), pLabel, pValue);
            if (diskonFaktur > 0) Row("Diskon Faktur", $"- {FormatRupiah(diskonFaktur)}", pLabel, pValue);
            if (expenses.Count > 0)
            {
                canvas?.DrawText("Biaya-biaya", margin, y, pLabel);
                y += pLabel.TextSize + 7;
                foreach (var exp in expenses)
                    Row("   " + (exp.detailName ?? "Biaya"), FormatRupiah(exp.expenseAmount), pLabel, pValue);
            }
            Row("Total Pajak (PPN)", FormatRupiah(pajak), pLabel, pValue);

            // ===== Band TOTAL =====
            y += 10;
            float bandH = 42;
            if (canvas != null)
            {
                pBg.Color = cCyanBg;
                var rect = new Android.Graphics.RectF(margin, y, width - margin, y + bandH);
                canvas.DrawRoundRect(rect, 12, 12, pBg);
                canvas.DrawText("TOTAL", margin + 16, CenterBaseline(y, bandH, pTotalLabel), pTotalLabel);
                string tv = FormatRupiah(total);
                float wv = pTotalVal.MeasureText(tv);
                canvas.DrawText(tv, width - margin - 16 - wv, CenterBaseline(y, bandH, pTotalVal), pTotalVal);
            }
            y += bandH + 12;

            // ===== Box tunai (dibayar & kembalian) =====
            if (isTunai)
            {
                float boxH = 58;
                if (canvas != null)
                {
                    pBg.Color = cCyanBg;
                    var rect = new Android.Graphics.RectF(margin, y, width - margin, y + boxH);
                    canvas.DrawRoundRect(rect, 12, 12, pBg);

                    float ry1 = y + 22;
                    canvas.DrawText("Tunai Dibayar", margin + 16, ry1, pBoxLabel);
                    string bv = FormatRupiah(bayar);
                    canvas.DrawText(bv, width - margin - 16 - pBoxVal.MeasureText(bv), ry1, pBoxVal);

                    float ry2 = y + 44;
                    canvas.DrawText("Kembalian", margin + 16, ry2, pBoxLabel);
                    string kv = FormatRupiah(kembalian);
                    canvas.DrawText(kv, width - margin - 16 - pBoxVal.MeasureText(kv), ry2, pBoxVal);
                }
                y += boxH + 12;
            }

            // ===== Metode =====
            Row("Metode Pembayaran", metode, pLabel, pValueB);

            // ===== Catatan =====
            if (!string.IsNullOrWhiteSpace(catatan))
            {
                y += 4;
                canvas?.DrawText("Catatan", margin, y, pLabel);
                y += pLabel.TextSize + 6;
                WrapLeft(catatan, pValue, width - 2 * margin);
            }

            Dashed(14, 20);

            // ===== Footer =====
            Center("Terima kasih telah berbelanja", pThanks, 8);

            Dashed(6, 22);

            if (!string.IsNullOrWhiteSpace(_company?.name)) Center(_company.name, pFootName, 6);

            string addressCity = "";
            if (!string.IsNullOrWhiteSpace(_company?.address)) addressCity += _company.address;
            if (!string.IsNullOrWhiteSpace(_company?.city))
                addressCity += (addressCity.Length > 0 ? ", " : "") + _company.city;
            if (!string.IsNullOrWhiteSpace(addressCity)) Center(addressCity, pFootInfo, 5);

            string contact = "";
            if (!string.IsNullOrWhiteSpace(_company?.phone)) contact += _company.phone;
            if (!string.IsNullOrWhiteSpace(_company?.email))
                contact += (contact.Length > 0 ? "  •  " : "") + _company.email;
            if (!string.IsNullOrWhiteSpace(contact)) Center(contact, pFootInfo, 8);

            Center($"Dicetak pada: {DateTime.Now.ToString("dd MMM yyyy HH:mm:ss", IdCulture)}", pFootDate, 4);

            return y;
        }

        // Pass 1: ukur tinggi
        float contentHeight = DrawAll(null);
        int pageHeight = (int)Math.Ceiling(contentHeight) + margin;

        using var document = new Android.Graphics.Pdf.PdfDocument();
        var pageInfo = new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(width, pageHeight, 1).Create();
        var page = document.StartPage(pageInfo);
        page.Canvas.DrawColor(Android.Graphics.Color.White);
        DrawAll(page.Canvas); // Pass 2: gambar
        document.FinishPage(page);

        using var ms = new MemoryStream();
        document.WriteTo(ms);
        document.Close();
        return ms.ToArray();
    }
#endif

    private void B_Skip_Clicked(object sender, EventArgs e)
    {
        // Kembali ke List-Faktur (instance baru agar status faktur ter-refresh)
        Application.Current.MainPage = new NavigationPage(new List_Faktur());
    }

    // ===================== Model tampilan item =====================
    private class StrukItem
    {
        public string NamaBarang { get; set; }
        public double quantity { get; set; }
        public double unitPrice { get; set; }
        public string satuan { get; set; }

        public string QtyAndPrice =>
            $"{quantity.ToString("0.##", IdCulture)} {satuan} x Rp {unitPrice.ToString("N0", IdCulture)}";
        public string FormattedLineTotal =>
            $"Rp {(quantity * unitPrice).ToString("N0", IdCulture)}";
    }

    // Model tampilan biaya (detailExpense)
    private class StrukExpense
    {
        public string detailName { get; set; }
        public double expenseAmount { get; set; }
        public string FormattedAmount => $"Rp {expenseAmount.ToString("N0", IdCulture)}";
    }

    // ===================== Response detail-receipt.php =====================
    private class DetailReceiptResponse
    {
        public string status { get; set; }
        public DetailReceiptData data { get; set; }
    }

    private class DetailReceiptData
    {
        public string number { get; set; }
        public double totalPayment { get; set; }
        public string charField2 { get; set; }   // kasir
        public string charField1 { get; set; }   // referensi (QRIS/VA)
        public double totalDiscount { get; set; }
        public string paymentMethod { get; set; }
        public string description { get; set; }
        public string transDate { get; set; }
        public string charField3 { get; set; } // nomor VA
        public double numericField1 { get; set; } // kembalian (tunai)
        public ReceiptCustomer customer { get; set; }
        public List<ReceiptDetailInvoice> detailInvoice { get; set; }
    }

    private class ReceiptCustomer
    {
        public string name { get; set; }
        public string customerNo { get; set; }
    }

    private class ReceiptDetailInvoice
    {
        public ReceiptInvoice invoice { get; set; }
        public List<ReceiptDetailDiscount> detailDiscount { get; set; }
    }

    private class ReceiptInvoice
    {
        public string number { get; set; }
    }

    private class ReceiptDetailDiscount
    {
        public double amount { get; set; }
        public ReceiptDiscountAccount account { get; set; }
    }

    private class ReceiptDiscountAccount
    {
        public string name { get; set; }
    }

    // ===================== Response detail-invoice.php =====================
    private class DetailInvoiceResponse
    {
        public string status { get; set; }
        public DetailInvoiceData data { get; set; }
    }

    private class DetailInvoiceData
    {
        public string toAddress { get; set; }      // alamat
        public Shipment shipment { get; set; }     // mode pengiriman
        public double tax1AmountBase { get; set; } // total pajak
        public string transDate { get; set; }
        public double cashDiscount { get; set; }   // total diskon faktur
        public string number { get; set; }
        public List<InvItem> detailItem { get; set; }
        public List<InvExpense> detailExpense { get; set; } // biaya-biaya
        public string status { get; set; }
        public double subTotal { get; set; }
        public string masterSalesmanName { get; set; }
        public double totalAmount { get; set; }
        public List<ReceiptHistory> receiptHistory { get; set; }
    }

    private class Shipment
    {
        public string name { get; set; }
    }

    private class InvExpense
    {
        public string detailName { get; set; }
        public double expenseAmount { get; set; }
    }

    private class InvItem
    {
        public ItemUnit itemUnit { get; set; }
        public double unitPrice { get; set; }
        public string salesmanName { get; set; }
        public InvItemInfo item { get; set; }
        public double quantity { get; set; }
    }

    private class ItemUnit
    {
        public string name { get; set; }
    }

    private class InvItemInfo
    {
        public int id { get; set; }
        public string shortName { get; set; }
        public string name { get; set; }
        public string no { get; set; }
    }

    private class ReceiptHistory
    {
        public string historyNumber { get; set; }
        public string historyPaymentName { get; set; }
    }

    // ===================== Response profile/company.php =====================
    private class CompanyProfileResponse
    {
        public string status { get; set; }
        public CompanyProfileData data { get; set; }
    }

    private class CompanyProfileData
    {
        public string name { get; set; }
        public string city { get; set; }
        public string email { get; set; }
        public string address { get; set; }
        public string phone { get; set; }
    }

    private async void TapPrint_Tapped(object sender, TappedEventArgs e)
    {

        if (sender is StackLayout press)
        {
            await press.FadeToAsync(0.3, 100); // Turunkan opacity ke 0.3 dalam 100ms
            await press.FadeToAsync(1, 200);   // Kembalikan opacity ke 1 dalam 200ms
        }
#if ANDROID
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Bluetooth>();
            }

            if (status != PermissionStatus.Granted)
            {
                await DisplayAlertAsync("Izin Ditolak", "Izin Bluetooth diperlukan untuk menghubungkan ke printer.", "OK");
                return;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error Permission", ex.Message, "OK");
            return;
        }

        BluetoothAdapter bluetoothAdapter = BluetoothAdapter.DefaultAdapter;
        if (bluetoothAdapter == null || !bluetoothAdapter.IsEnabled)
        {
            await DisplayAlertAsync("Error", "Bluetooth tidak tersedia atau belum diaktifkan.", "OK");
            return;
        }

        var bondedDevices = bluetoothAdapter.BondedDevices;
        if (bondedDevices == null || bondedDevices.Count == 0)
        {
            await DisplayAlertAsync("Error", "Tidak ada printer Bluetooth yang di-pairing.", "OK");
            return;
        }

        var deviceNames = bondedDevices.Select(d => d.Name).ToArray();
        string selectedPrinter = await DisplayActionSheetAsync("Pilih Printer Bluetooth", "Batal", null, deviceNames);

        if (selectedPrinter == "Batal" || string.IsNullOrEmpty(selectedPrinter))
            return;

        string paperSizeStr = await DisplayActionSheetAsync("Pilih Ukuran Kertas", "Batal", null, "58mm", "80mm");
        if (paperSizeStr == "Batal" || string.IsNullOrEmpty(paperSizeStr))
            return;
        
        int paperSize = paperSizeStr == "58mm" ? 32 : 48;
        
        BluetoothDevice device = bondedDevices.FirstOrDefault(d => d.Name == selectedPrinter);
        if (device != null)
        {
            await ExecutePrint(device, paperSize);
        }
#else
        await DisplayAlertAsync("Error", "Bluetooth hanya didukung di Android!", "OK");
#endif
    }

    private async void TapWA_Tapped(object sender, TappedEventArgs e)
    {

        if (sender is StackLayout press)
        {
            await press.FadeToAsync(0.3, 100); // Turunkan opacity ke 0.3 dalam 100ms
            await press.FadeToAsync(1, 200);   // Kembalikan opacity ke 1 dalam 200ms
        }
#if ANDROID
        // Pastikan data struk sudah dimuat
        if (_invoice == null && _receipt == null)
        {
            await DisplayAlertAsync("WhatsApp", "Data struk belum termuat. Coba lagi sebentar.", "OK");
            return;
        }

        // 1. Minta nomor HP tujuan
        string inputNomor = await DisplayPromptAsync(
            "Kirim via WhatsApp",
            "Masukkan nomor WhatsApp tujuan:",
            accept: "Kirim",
            cancel: "Batal",
            placeholder: "08xxxxxxxxxx",
            keyboard: Keyboard.Telephone);

        if (string.IsNullOrWhiteSpace(inputNomor))
            return;

        string nomorWa = NormalisasiNomorWa(inputNomor);
        if (nomorWa.Length < 10)
        {
            await DisplayAlertAsync("Nomor Tidak Valid", "Periksa kembali nomor WhatsApp tujuan.", "OK");
            return;
        }

        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StrukLoading.IsRunning = true;
                StrukLoading.IsVisible = true;
            });

            // 2. Generate PDF (PdfDocument bawaan Android) & simpan ke storage
            byte[] pdfBytes = await Task.Run(() => BuildReceiptPdfAndroid());

            string fileName = $"Struk-{(string.IsNullOrWhiteSpace(_receiptNumber) ? _invoiceNoStr : _receiptNumber)}.pdf"
                .Replace("/", "-").Replace("\\", "-").Replace(" ", "");
            string filePath = Path.Combine(FileSystem.CacheDirectory, fileName);
            File.WriteAllBytes(filePath, pdfBytes);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                StrukLoading.IsRunning = false;
                StrukLoading.IsVisible = false;
            });

            // 3. Susun pesan teks
            string pesan = SusunPesanWa();

            // 4. Kirim ke WhatsApp dengan PDF terlampir (extra "jid" -> langsung ke nomor tujuan)
            bool terkirim = KirimWhatsAppAndroid(filePath, nomorWa, pesan);
            if (!terkirim)
            {
                // Fallback: buka chat nomor via wa.me (tanpa lampiran) + bagikan file lewat share sheet
                await Launcher.OpenAsync(new Uri($"https://wa.me/{nomorWa}?text={Uri.EscapeDataString(pesan)}"));
                await Share.RequestAsync(new ShareFileRequest
                {
                    Title = "Bagikan Struk PDF",
                    File = new ShareFile(filePath)
                });
            }
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StrukLoading.IsRunning = false;
                StrukLoading.IsVisible = false;
            });
            await DisplayAlertAsync("Gagal Kirim WhatsApp", ex.Message, "OK");
        }
#else
        await DisplayAlertAsync("WhatsApp", "Kirim struk via WhatsApp hanya didukung di Android.", "OK");
#endif
    }
}
