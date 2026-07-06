using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Report;

public partial class Receipt : ContentPage
{
	// Culture untuk format angka ribuan (mis. 150.000)
	private static readonly CultureInfo IdCulture = new CultureInfo("id-ID");

	// Data company untuk kop laporan
	private CompanyProfileData _company;

	// Hasil laporan terakhir (dipakai saat generate PDF / kirim WA)
	private List<DateGroup> _reportGroups;
	private ReportSummary _reportSummary;
	private DateTime _reportStart;
	private DateTime _reportEnd;

	public Receipt()
	{
		InitializeComponent();

		// Default rentang: tanggal 1 awal bulan sekarang s/d hari ini
		var today = DateTime.Today;
		DP_startdate.Date = new DateTime(today.Year, today.Month, 1);
		DP_enddate.Date = today;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadKasirData();
		await LoadCompanyAsync();
	}

	// Ambil profil usaha untuk kop laporan
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
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("Gagal memuat profil usaha: " + ex.Message);
		}
	}

	private async Task LoadKasirData()
	{
		try
		{
			// 1. Ambil token dari Preferences
			string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();

			if (string.IsNullOrEmpty(cleanToken))
			{
				System.Diagnostics.Debug.WriteLine("Token tidak ditemukan.");
				return;
			}

			// 2. Susun URL API untuk mengambil daftar kasir lokal
			string apiUrl = $"{App.API_HOST}kasir/list-lokal.php";

			using (var client = new HttpClient())
			{
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

				// 3. Tarik data dari server
				var response = await client.GetAsync(apiUrl);

				if (response.IsSuccessStatusCode)
				{
					string responseContent = await response.Content.ReadAsStringAsync();

					// Jaga-jaga jika PHP mengembalikan HTML error, bukan JSON
					if (responseContent.StartsWith("<"))
					{
						System.Diagnostics.Debug.WriteLine("Respon server bukan JSON: " + responseContent);
						return;
					}

					// 4. Konversi JSON ke object C#
					var apiResult = JsonConvert.DeserializeObject<KasirResponse>(responseContent);

					if (apiResult != null && apiResult.data != null)
					{
						// 5. Masukkan data ke dalam Picker di Main Thread
						MainThread.BeginInvokeOnMainThread(() =>
						{
							PickerNamaKasir.ItemsSource = apiResult.data;
						});
					}
				}
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("Gagal memuat data kasir: " + ex.Message);
		}
	}

	private async void BtnGenerate_Clicked(object sender, EventArgs e)
	{
		DateTime start = DP_startdate.Date.GetValueOrDefault(DateTime.Today);
		DateTime end = DP_enddate.Date.GetValueOrDefault(DateTime.Today);

		// Validasi rentang tanggal
		if (end < start)
		{
			await DisplayAlertAsync("Peringatan", "Tanggal akhir tidak boleh lebih awal dari tanggal mulai.", "OK");
			return;
		}

		BtnGenerate.IsEnabled = false;
		BtnGenerate.Text = "MEMUAT...";

		try
		{
			var receipts = await LoadReceiptData(start, end);
			if (receipts == null)
			{
				await DisplayAlertAsync("Gagal", "Tidak dapat memuat data penerimaan dari server.", "OK");
				return;
			}

			// Filter berdasarkan kasir yang dipilih (charField2 = "1 - Administrator")
			string namaKasir = "Semua Kasir";
			if (PickerNamaKasir.SelectedItem is KasirData kasir)
			{
				namaKasir = kasir.username;
				receipts = receipts.Where(r => r.charField2 == kasir.DisplayName).ToList();
			}

			RenderReport(receipts, start, end, namaKasir);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("Gagal generate report: " + ex.Message);
			await DisplayAlertAsync("Error", "Terjadi kesalahan saat menyusun laporan.", "OK");
		}
		finally
		{
			BtnGenerate.IsEnabled = true;
			BtnGenerate.Text = "GENERATE REPORT";
		}
	}

	// Tarik seluruh penerimaan pada rentang tanggal, manual paging (limit=100 per halaman)
	private async Task<List<ReceiptData>> LoadReceiptData(DateTime start, DateTime end)
	{
		string cleanToken = Preferences.Get("TOKEN_KEY", "").Replace("Bearer ", "").Trim();
		if (string.IsNullOrEmpty(cleanToken))
		{
			System.Diagnostics.Debug.WriteLine("Token tidak ditemukan.");
			return null;
		}

		var all = new List<ReceiptData>();
		string startParam = start.ToString("yyyy-MM-dd");
		string endParam = end.ToString("yyyy-MM-dd");
		const int limit = 100;
		int page = 1;
		bool hasMore = true;

		using (var client = new HttpClient())
		{
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cleanToken);

			while (hasMore)
			{
				string apiUrl = $"{App.API_HOST}penerimaan-jual/list-receipt.php?start_date={startParam}&end_date={endParam}&limit={limit}&page={page}";

				var response = await client.GetAsync(apiUrl);
				if (!response.IsSuccessStatusCode)
					return all.Count > 0 ? all : null;

				string responseContent = await response.Content.ReadAsStringAsync();
				if (responseContent.StartsWith("<"))
				{
					System.Diagnostics.Debug.WriteLine("Respon server bukan JSON: " + responseContent);
					return all.Count > 0 ? all : null;
				}

				var apiResult = JsonConvert.DeserializeObject<ReceiptResponse>(responseContent);
				if (apiResult == null || apiResult.data == null || apiResult.data.Count == 0)
					break;

				all.AddRange(apiResult.data);

				// Lanjut paging hanya jika halaman ini penuh
				hasMore = apiResult.data.Count == limit;
				page++;
			}
		}

		return all;
	}

	private void RenderReport(List<ReceiptData> receipts, DateTime start, DateTime end, string namaKasir)
	{
		string periode = $"Per Periode {start:dd/MM/yyyy} - {end:dd/MM/yyyy}";

		if (receipts == null || receipts.Count == 0)
		{
			ReportCollection.ItemsSource = null;
			ReportBorder.IsVisible = false;
			LblEmptyReport.Text = "Tidak ada data penerimaan pada periode ini.";
			LblEmptyReport.IsVisible = true;
			return;
		}

		// Kelompokkan per tanggal (urut menaik), lalu per metode pembayaran
		var groups = receipts
			.GroupBy(r => r.transDate)
			.OrderBy(g => ParseTransDate(g.Key))
			.Select(dateGroup =>
			{
				var dg = new DateGroup
				{
					DateLabel = dateGroup.Key,
					TotalCount = dateGroup.Count(),
					TotalAmount = dateGroup.Sum(r => r.totalPayment)
				};
				dg.AddRange(dateGroup
					.GroupBy(r => r.paymentMethodName)
					.Select(m => new MethodRow
					{
						MethodName = m.Key,
						Count = m.Count(),
						Amount = m.Sum(r => r.totalPayment)
					}));
				return dg;
			})
			.ToList();

		// Ringkasan keseluruhan + kop usaha
		var summary = new ReportSummary
		{
			Periode = periode,
			TransactionCount = receipts.Count,
			GrandTotalAmount = receipts.Sum(r => r.totalPayment),
			NamaKasir = namaKasir,
			Methods = receipts
				.GroupBy(r => r.paymentMethodName)
				.Select(m => new MethodRow
				{
					MethodName = m.Key,
					Amount = m.Sum(r => r.totalPayment)
				})
				.ToList(),
			CompanyName = _company?.name,
			CompanyAddress = _company?.address,
			CompanyRegion = ComposeRegion(_company),
			CompanyContact = ComposeContact(_company)
		};

		// Header & Footer membaca BindingContext; item grup membaca konteksnya sendiri
		ReportCollection.BindingContext = summary;
		ReportCollection.ItemsSource = groups;

		// Simpan untuk PDF / WhatsApp
		_reportGroups = groups;
		_reportSummary = summary;
		_reportStart = start;
		_reportEnd = end;

		LblEmptyReport.IsVisible = false;
		ReportBorder.IsVisible = true;
	}

	// Gabungkan district/province/country jadi satu baris kop
	private static string ComposeRegion(CompanyProfileData c)
	{
		if (c?.district == null) return null;
		var parts = new[] { c.district.district, c.district.province, c.district.country }
			.Where(s => !string.IsNullOrWhiteSpace(s));
		string region = string.Join(", ", parts);
		return string.IsNullOrWhiteSpace(region) ? null : region;
	}

	// Gabungkan telepon & email jadi satu baris kop
	private static string ComposeContact(CompanyProfileData c)
	{
		if (c == null) return null;
		var parts = new List<string>();
		if (!string.IsNullOrWhiteSpace(c.phone)) parts.Add($"Telp: {c.phone}");
		if (!string.IsNullOrWhiteSpace(c.email)) parts.Add($"Email: {c.email}");
		return parts.Count > 0 ? string.Join("  •  ", parts) : null;
	}

	private static DateTime ParseTransDate(string transDate)
	{
		return DateTime.TryParseExact(transDate, "dd/MM/yyyy", CultureInfo.InvariantCulture,
			DateTimeStyles.None, out var dt) ? dt : DateTime.MinValue;
	}

	// ===================== Aksi: Download PDF & WhatsApp =====================

	private string ReportFileName()
	{
		return $"Laporan-Penerimaan-{_reportStart:yyyyMMdd}-{_reportEnd:yyyyMMdd}.pdf"
			.Replace("/", "-").Replace("\\", "-").Replace(" ", "");
	}

	private async void BtnDownloadPdf_Clicked(object sender, EventArgs e)
	{
		if (_reportSummary == null)
		{
			await DisplayAlertAsync("Laporan Kosong", "Silakan tekan GENERATE REPORT terlebih dahulu.", "OK");
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
				Title = "Simpan / Bagikan Laporan PDF",
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

	private async void BtnWhatsApp_Clicked(object sender, EventArgs e)
	{
		if (_reportSummary == null)
		{
			await DisplayAlertAsync("Laporan Kosong", "Silakan tekan GENERATE REPORT terlebih dahulu.", "OK");
			return;
		}

#if ANDROID
		// 1. Minta nomor WhatsApp tujuan (input manual)
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
			// 2. Generate PDF & simpan ke cache
			byte[] pdfBytes = await Task.Run(() => BuildReportPdfAndroid());
			string filePath = Path.Combine(FileSystem.CacheDirectory, ReportFileName());
			File.WriteAllBytes(filePath, pdfBytes);

			// 3. Susun pesan teks
			string pesan = SusunPesanWa();

			// 4. Kirim ke WhatsApp dengan PDF terlampir (extra "jid" -> langsung ke nomor tujuan)
			bool terkirim = KirimWhatsAppAndroid(filePath, nomorWa, pesan);
			if (!terkirim)
			{
				// Fallback: buka chat via wa.me + bagikan file lewat share sheet
				await Launcher.OpenAsync(new Uri($"https://wa.me/{nomorWa}?text={Uri.EscapeDataString(pesan)}"));
				await Share.RequestAsync(new ShareFileRequest
				{
					Title = "Bagikan Laporan PDF",
					File = new ShareFile(filePath)
				});
			}
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Gagal Kirim WhatsApp", ex.Message, "OK");
		}
#else
		await DisplayAlertAsync("Tidak Didukung", "Kirim laporan via WhatsApp hanya didukung di Android.", "OK");
#endif
	}

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
		sb.AppendLine($"*{_company?.name ?? "Laporan Penerimaan Kasir"}*");
		sb.AppendLine("Laporan Penerimaan Berdasarkan Kasir");
		sb.AppendLine();
		if (_reportSummary != null)
		{
			sb.AppendLine(_reportSummary.Periode);
			sb.AppendLine($"Nama Kasir: {_reportSummary.NamaKasir}");
			sb.AppendLine($"Jumlah Transaksi: {_reportSummary.JumlahTransaksi}");
			sb.AppendLine($"Total Penerimaan: Rp {_reportSummary.GrandTotal}");
		}
		sb.AppendLine();
		sb.AppendLine("Laporan lengkap terlampir dalam bentuk PDF.");
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

			string paket = ResolveWhatsAppPackage(context);
			if (paket == null)
				return false; // WhatsApp tidak terpasang -> caller pakai fallback

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

	// ===================== Generate PDF laporan (A4 portrait, multi-halaman) =====================
	// Mengikuti pendekatan Print.xaml.cs: gambar langsung ke PdfDocument bawaan Android
	// (QuestPDF tidak punya native runtime di Android). Konten dipecah jadi blok agar bisa
	// mengalir ke halaman A4 berikutnya bila melebihi tinggi satu halaman.
	private byte[] BuildReportPdfAndroid()
	{
		var groups = _reportGroups ?? new List<DateGroup>();
		var summary = _reportSummary;

		// A4 portrait @72dpi
		const int pageW = 595;
		const int pageH = 842;
		const float margin = 40f;
		float contentTop = margin;
		float contentBottom = pageH - margin;

		// ===== Warna =====
		var cBlack = Android.Graphics.Color.Argb(255, 26, 26, 26);
		var cValue = Android.Graphics.Color.Argb(255, 51, 51, 51);
		var cGrey = Android.Graphics.Color.Argb(255, 119, 119, 119);
		var cBand = Android.Graphics.Color.Argb(255, 232, 232, 232);
		var cLine = Android.Graphics.Color.Argb(255, 51, 51, 51);
		var cSoft = Android.Graphics.Color.Argb(255, 204, 204, 204);

		// ===== Paints =====
		var pCompany = new Android.Graphics.Paint { Color = cBlack, TextSize = 18, FakeBoldText = true, AntiAlias = true };
		var pCompanyInfo = new Android.Graphics.Paint { Color = cGrey, TextSize = 10, AntiAlias = true };
		var pTitle = new Android.Graphics.Paint { Color = cBlack, TextSize = 15, FakeBoldText = true, AntiAlias = true };
		var pPeriode = new Android.Graphics.Paint { Color = cGrey, TextSize = 11, AntiAlias = true, TextSkewX = -0.25f };
		var pColHead = new Android.Graphics.Paint { Color = cBlack, TextSize = 11, FakeBoldText = true, AntiAlias = true };
		var pDate = new Android.Graphics.Paint { Color = cBlack, TextSize = 11, AntiAlias = true };
		var pBody = new Android.Graphics.Paint { Color = cValue, TextSize = 11, AntiAlias = true };
		var pBodyB = new Android.Graphics.Paint { Color = cBlack, TextSize = 11, FakeBoldText = true, AntiAlias = true };
		var pFootDate = new Android.Graphics.Paint { Color = cGrey, TextSize = 9, AntiAlias = true };
		var pBg = new Android.Graphics.Paint { AntiAlias = true };
		var pLinePaint = new Android.Graphics.Paint { Color = cLine, StrokeWidth = 1f, AntiAlias = true };
		var pSoftLine = new Android.Graphics.Paint { Color = cSoft, StrokeWidth = 1f, AntiAlias = true };

		// Posisi kolom
		float xLeft = margin;
		float xRightTotal = pageW - margin;          // tepi kanan kolom Total
		float xRightJumlah = pageW - margin - 150;   // tepi kanan kolom Jumlah

		float TextH(Android.Graphics.Paint p) => p.Descent() - p.Ascent();
		float Baseline(float top, Android.Graphics.Paint p) => top - p.Ascent();

		// Daftar blok: tinggi + aksi gambar pada posisi top tertentu
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
		void AddLeft(string text, Android.Graphics.Paint p, float gap)
		{
			float h = TextH(p) + gap;
			blocks.Add((h, (canvas, top) => canvas.DrawText(text ?? "", xLeft, Baseline(top, p), p)));
		}
		void AddRow3(string a, string b, string c, Android.Graphics.Paint pa, Android.Graphics.Paint pb, Android.Graphics.Paint pc, float gap)
		{
			float h = TextH(pa) + gap;
			blocks.Add((h, (canvas, top) =>
			{
				float bl = Baseline(top, pa);
				if (a != null) canvas.DrawText(a, xLeft, bl, pa);
				if (b != null) canvas.DrawText(b, xRightJumlah - pb.MeasureText(b), bl, pb);
				if (c != null) canvas.DrawText(c, xRightTotal - pc.MeasureText(c), bl, pc);
			}));
		}
		void AddRow2(string a, string val, Android.Graphics.Paint pa, Android.Graphics.Paint pv, float gap, bool centerLabel = false)
		{
			float h = TextH(pa) + gap;
			blocks.Add((h, (canvas, top) =>
			{
				float bl = Baseline(top, pa);
				if (centerLabel)
				{
					float midRight = xRightJumlah;
					canvas.DrawText(a ?? "", (xLeft + midRight) / 2f - pa.MeasureText(a ?? "") / 2f, bl, pa);
				}
				else
				{
					canvas.DrawText(a ?? "", xLeft, bl, pa);
				}
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
				canvas.DrawText(label, (xLeft + xRightJumlah) / 2f - pBodyB.MeasureText(label) / 2f, bl, pBodyB);
				if (val != null) canvas.DrawText(val, xRightTotal - 8 - pBodyB.MeasureText(val), bl, pBodyB);
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

		// ===== Kop usaha =====
		if (summary != null && summary.HasCompany)
		{
			AddCenter(summary.CompanyName, pCompany, 4);
			AddCenter(summary.CompanyAddress, pCompanyInfo, 2);
			AddCenter(summary.CompanyRegion, pCompanyInfo, 2);
			AddCenter(summary.CompanyContact, pCompanyInfo, 6);
			AddSeparator(2, 14, soft: true);
		}

		// ===== Judul & periode =====
		AddCenter("Laporan Penerimaan Berdasarkan Kasir", pTitle, 4);
		AddCenter(summary?.Periode, pPeriode, 12);

		// ===== Header kolom =====
		AddRow3("Metode Pembayaran", "Jumlah", "Total", pColHead, pColHead, pColHead, 6);
		AddSeparator(2, 10);

		// ===== Per tanggal =====
		foreach (var dg in groups)
		{
			AddLeft(dg.DateLabel, pDate, 6);
			foreach (var m in dg)
				AddRow3("   " + m.MethodName, m.DisplayCount, m.DisplayAmount, pBody, pBody, pBody, 5);
			AddRow3("Total", dg.DisplayCount, dg.DisplayTotal, pBodyB, pBodyB, pBodyB, 5);
			AddSpacer(12);
		}

		AddSeparator(2, 10);

		// ===== Ringkasan =====
		if (summary != null)
		{
			AddLeft("Ringkasan", pBodyB, 6);
			AddRow2("Jumlah Transaksi", summary.JumlahTransaksi, pBody, pBody, 5);
			foreach (var m in summary.Methods)
				AddRow2(m.MethodName, m.DisplayAmount, pBody, pBody, 5);

			AddSpacer(6);
			AddBand("Total", summary.GrandTotal, 8);
			AddRow2("Nama Kasir", summary.NamaKasir, pBody, pBodyB, 5, centerLabel: true);
		}

		AddSpacer(16);
		AddCenter($"Dicetak pada: {DateTime.Now.ToString("dd MMM yyyy HH:mm:ss", IdCulture)}", pFootDate, 0);

		// ===== Susun ke halaman A4 =====
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

	// ---------- DTO ----------

	public class KasirResponse
	{
		public string status { get; set; }
		public string message { get; set; }
		public List<KasirData> data { get; set; }
	}

	public class KasirData
	{
		public int id_users { get; set; }
		public string username { get; set; }

		// Properti tampilan untuk Picker (ItemDisplayBinding), mis. "1 - Administrator"
		public string DisplayName => $"{id_users} - {username}";
	}

	public class ReceiptResponse
	{
		public string status { get; set; }
		public string message { get; set; }
		public List<ReceiptData> data { get; set; }
	}

	public class ReceiptData
	{
		public string number { get; set; }
		public double totalPayment { get; set; }
		public string charField2 { get; set; }      // kasir, mis. "1 - Administrator"
		public string transDate { get; set; }       // "21/06/2026"
		public string paymentMethodName { get; set; }
		public ReceiptCustomer customer { get; set; }
	}

	public class ReceiptCustomer
	{
		public string name { get; set; }
		public string customerNo { get; set; }
	}

	// ---------- View-model untuk CollectionView ----------

	// Grup per tanggal; harus berupa koleksi agar bisa dipakai IsGrouped CollectionView.
	public class DateGroup : List<MethodRow>
	{
		public string DateLabel { get; set; }   // mis. "21/06/2026"
		public int TotalCount { get; set; }
		public double TotalAmount { get; set; }

		// Dipakai GroupFooterTemplate
		public string DisplayCount => TotalCount.ToString("N0", IdCulture);
		public string DisplayTotal => TotalAmount.ToString("N0", IdCulture);
	}

	// Baris satu metode pembayaran (dipakai item grup & ringkasan).
	public class MethodRow
	{
		public string MethodName { get; set; }
		public int Count { get; set; }
		public double Amount { get; set; }

		public string DisplayMethod => "   " + MethodName;          // sedikit indent di tabel
		public string DisplayCount => Count.ToString("N0", IdCulture);
		public string DisplayAmount => Amount.ToString("N0", IdCulture);
	}

	// Konteks Header/Footer CollectionView.
	public class ReportSummary
	{
		public string Periode { get; set; }
		public int TransactionCount { get; set; }
		public double GrandTotalAmount { get; set; }
		public string NamaKasir { get; set; }
		public List<MethodRow> Methods { get; set; }

		// Kop usaha
		public string CompanyName { get; set; }
		public string CompanyAddress { get; set; }
		public string CompanyRegion { get; set; }
		public string CompanyContact { get; set; }
		public bool HasCompany => !string.IsNullOrWhiteSpace(CompanyName);

		public string JumlahTransaksi => TransactionCount.ToString("N0", IdCulture);
		public string GrandTotal => GrandTotalAmount.ToString("N0", IdCulture);
	}

	// ---------- Response profile/company.php ----------

	public class CompanyProfileResponse
	{
		public string status { get; set; }
		public CompanyProfileData data { get; set; }
	}

	public class CompanyProfileData
	{
		public string name { get; set; }
		public string email { get; set; }
		public string address { get; set; }
		public string phone { get; set; }
		public CompanyDistrict district { get; set; }
	}

	public class CompanyDistrict
	{
		public string country { get; set; }
		public string province { get; set; }
		public string district { get; set; }
	}
}
