using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Newtonsoft.Json;

namespace MyPosAccurate2026.Sales;

public class LokasiTerpilihEventArgs : EventArgs
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Alamat { get; set; }
    public bool DariGps { get; set; }
}

// Daftar kota beserta titik pusatnya.
public class Kota
{
    public string Nama { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public bool IsMain { get; set; }
    public override string ToString() => Nama;
}

public partial class Maps : ContentPage
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Titik terpilih saat ini
    private double _lat;
    private double _lng;
    private string _alamat = "";
    private bool _fromGps;          // true bila titik berasal dari GPS perangkat
    private bool _isReady;          // peta Leaflet sudah siap
    private bool _initialized;      // auto-deteksi GPS hanya sekali
    private bool _suppressFilter;   // cegah loop saat set teks programatik

    // Event opsional supaya halaman pemanggil bisa menerima hasil pilihan.
    public event EventHandler<LokasiTerpilihEventArgs> LokasiDipilih;

    // Kota utama tampil paling atas, sisanya menyusul.
    private readonly List<Kota> _allCities = new()
    {
        new Kota { Nama = "Jakarta",    Lat = -6.2088,  Lng = 106.8456, IsMain = true },
        new Kota { Nama = "Bandung",    Lat = -6.9175,  Lng = 107.6191, IsMain = true },
        new Kota { Nama = "Yogyakarta", Lat = -7.7956,  Lng = 110.3695, IsMain = true },
        new Kota { Nama = "Surabaya",   Lat = -7.2575,  Lng = 112.7521, IsMain = true },
        new Kota { Nama = "Samarinda",  Lat = -0.5022,  Lng = 117.1536, IsMain = true },

        new Kota { Nama = "Bogor",       Lat = -6.5950,  Lng = 106.8166 },
        new Kota { Nama = "Depok",       Lat = -6.4025,  Lng = 106.7942 },
        new Kota { Nama = "Tangerang",   Lat = -6.1783,  Lng = 106.6319 },
        new Kota { Nama = "Bekasi",      Lat = -6.2383,  Lng = 106.9756 },
        new Kota { Nama = "Semarang",    Lat = -6.9667,  Lng = 110.4167 },
        new Kota { Nama = "Malang",      Lat = -7.9666,  Lng = 112.6326 },
        new Kota { Nama = "Denpasar",    Lat = -8.6500,  Lng = 115.2167 },
        new Kota { Nama = "Medan",       Lat =  3.5952,  Lng =  98.6722 },
        new Kota { Nama = "Padang",      Lat = -0.9471,  Lng = 100.4172 },
        new Kota { Nama = "Pekanbaru",   Lat =  0.5071,  Lng = 101.4478 },
        new Kota { Nama = "Palembang",   Lat = -2.9761,  Lng = 104.7754 },
        new Kota { Nama = "Batam",       Lat =  1.0456,  Lng = 104.0305 },
        new Kota { Nama = "Pontianak",   Lat = -0.0263,  Lng = 109.3425 },
        new Kota { Nama = "Banjarmasin", Lat = -3.3186,  Lng = 114.5944 },
        new Kota { Nama = "Balikpapan",  Lat = -1.2379,  Lng = 116.8529 },
        new Kota { Nama = "Makassar",    Lat = -5.1477,  Lng = 119.4327 },
        new Kota { Nama = "Manado",      Lat =  1.4748,  Lng = 124.8421 },
        new Kota { Nama = "Jayapura",    Lat = -2.5337,  Lng = 140.7181 },
    };

    private readonly ObservableCollection<Kota> _filtered = new();

    public Maps()
    {
        InitializeComponent();
        CityList.ItemsSource = _filtered;
        FilterCities(null);
        MapWebView.Source = new HtmlWebViewSource { Html = BuildLeafletHtml() };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;

        // Deteksi kota saat ini via GPS (best-effort, tanpa mengganggu bila ditolak).
        await DetectGpsAsync(manual: false);
    }

    // ===================== Pemilih kota (searchable) =====================
    private void CitySearch_Focused(object sender, FocusEventArgs e) => ShowCityList(true);

    private void CitySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFilter) return;
        FilterCities(e.NewTextValue);
        ShowCityList(true);
    }

    private void SearchEntry_Focused(object sender, FocusEventArgs e) => ShowCityList(false);

    private void ShowCityList(bool show) => CityListBorder.IsVisible = show;

    private void FilterCities(string query)
    {
        _filtered.Clear();
        IEnumerable<Kota> src = _allCities;
        if (!string.IsNullOrWhiteSpace(query))
            src = _allCities.Where(k => k.Nama.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));
        foreach (var k in src) _filtered.Add(k);
    }

    private async void CityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Kota k) return;
        CityList.SelectedItem = null;

        _suppressFilter = true;
        CitySearch.Text = k.Nama;
        _suppressFilter = false;
        ShowCityList(false);
        CitySearch.Unfocus();

        _fromGps = false;
        await PindahPetaAsync(k.Lat, k.Lng, 13);
    }

    

    // ===================== GPS: lokasi saat ini =====================
   

    private async Task DetectGpsAsync(bool manual)
    {
        SetLoading(true);
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
            {
                if (manual)
                    await DisplayAlertAsync("Izin Lokasi", "Izin lokasi diperlukan untuk mendeteksi posisi Anda.", "OK");
                return;
            }

            var loc = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));

            if (loc == null)
            {
                if (manual)
                    await DisplayAlertAsync("Lokasi Tidak Tersedia", "Tidak bisa mendapatkan lokasi. Pastikan GPS aktif.", "OK");
                return;
            }

            // Tampilkan nama kota terdekat di kotak pilih kota (sekadar info).
            var nearest = NearestCity(loc.Latitude, loc.Longitude);
            _suppressFilter = true;
            CitySearch.Text = nearest?.Nama ?? "Lokasi Saya";
            _suppressFilter = false;
            ShowCityList(false);

            _fromGps = true;
            if (await WaitMapReadyAsync())
                await MapWebView.EvaluateJavaScriptAsync(
                    $"setCity({loc.Latitude.ToString(Inv)},{loc.Longitude.ToString(Inv)},16)");

            await ReverseGeocodeAsync(loc.Latitude, loc.Longitude);
        }
        catch (Exception ex)
        {
            if (manual)
                await DisplayAlertAsync("Gagal Deteksi Lokasi", ex.Message, "OK");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private Kota NearestCity(double lat, double lng)
    {
        Kota best = null;
        double bestD = double.MaxValue;
        foreach (var k in _allCities)
        {
            double d = Haversine(lat, lng, k.Lat, k.Lng);
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        double R = 6371; // km
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    // Pindahkan & taruh pin di peta lewat JS, lalu reverse-geocode di C#.
    private async Task PindahPetaAsync(double lat, double lng, int zoom)
    {
        if (await WaitMapReadyAsync())
        {
            try
            {
                await MapWebView.EvaluateJavaScriptAsync(
                    $"setCity({lat.ToString(Inv)},{lng.ToString(Inv)},{zoom})");
            }
            catch { /* abaikan */ }
        }
        await ReverseGeocodeAsync(lat, lng);
    }

    private async Task<bool> WaitMapReadyAsync(int timeoutMs = 4000)
    {
        int waited = 0;
        while (!_isReady && waited < timeoutMs)
        {
            await Task.Delay(100);
            waited += 100;
        }
        return _isReady;
    }

    // ===================== Intersepsi pesan dari peta =====================
    private async void MapWebView_Navigating(object sender, WebNavigatingEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Url)) return;

        if (e.Url.StartsWith("app://ready", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            _isReady = true;
            return;
        }

        if (e.Url.StartsWith("app://pick", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            var (lat, lng) = ParseLatLng(e.Url);
            _fromGps = false; // titik dipilih manual di peta
            await ReverseGeocodeAsync(lat, lng);
        }
    }

    private static (double lat, double lng) ParseLatLng(string url)
    {
        double lat = 0, lng = 0;
        int q = url.IndexOf('?');
        if (q < 0) return (lat, lng);

        foreach (var part in url.Substring(q + 1).Split('&'))
        {
            var kv = part.Split('=');
            if (kv.Length != 2) continue;
            if (kv[0] == "lat") double.TryParse(kv[1], NumberStyles.Float, Inv, out lat);
            else if (kv[0] == "lng") double.TryParse(kv[1], NumberStyles.Float, Inv, out lng);
        }
        return (lat, lng);
    }

    // ===================== Reverse geocoding (titik -> alamat) =====================
    private async Task ReverseGeocodeAsync(double lat, double lng)
    {
        _lat = lat;
        _lng = lng;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            LabelLat.Text = lat.ToString("0.######", Inv);
            LabelLng.Text = lng.ToString("0.######", Inv);
            LabelAlamat.Text = "Mengambil alamat...";
            LabelSumber.Text = _fromGps ? "GPS" : "";
        });

        SetLoading(true);
        try
        {
            string url = $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&accept-language=id&lat={lat.ToString(Inv)}&lon={lng.ToString(Inv)}";
            var place = await GetNominatimAsync<NominatimPlace>(url);
            _alamat = place?.display_name ?? "(alamat tidak ditemukan)";
        }
        catch
        {
            _alamat = "(gagal mengambil alamat)";
        }
        finally
        {
            SetLoading(false);
            MainThread.BeginInvokeOnMainThread(() => LabelAlamat.Text = _alamat);
        }
    }

    // ===================== Tombol =====================
    private async void B_Salin_Clicked(object sender, EventArgs e)
    {
        if (_lat == 0 && _lng == 0)
        {
            await DisplayAlertAsync("Belum Ada Lokasi", "Pilih titik di peta terlebih dahulu.", "OK");
            return;
        }

        string teks = $"{_alamat}\nLat: {_lat.ToString("0.######", Inv)}, Long: {_lng.ToString("0.######", Inv)}";
        await Clipboard.SetTextAsync(teks);
        await DisplayAlertAsync("Disalin", "Alamat & koordinat telah disalin ke clipboard.", "OK");
    }

    private async void B_Gunakan_Clicked(object sender, EventArgs e)
    {
        if (_lat == 0 && _lng == 0)
        {
            await DisplayAlertAsync("Belum Ada Lokasi", "Pilih titik di peta terlebih dahulu.", "OK");
            return;
        }

        LokasiDipilih?.Invoke(this, new LokasiTerpilihEventArgs
        {
            Latitude = _lat,
            Longitude = _lng,
            Alamat = _alamat,
            DariGps = _fromGps
        });

        if (Navigation?.NavigationStack?.Count > 1)
            await Navigation.PopAsync();
    }

    private void SetLoading(bool on) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Loading.IsRunning = on;
            Loading.IsVisible = on;
        });

    // ===================== Nominatim helper =====================
    private static async Task<T> GetNominatimAsync<T>(string url)
    {
        using var client = new HttpClient();
        // Nominatim mewajibkan User-Agent yang mengidentifikasi aplikasi.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MPosAccurate/1.0 (POS Accurate Mobile)");
        client.Timeout = TimeSpan.FromSeconds(20);

        var content = await client.GetStringAsync(url);
        if (string.IsNullOrWhiteSpace(content) || content.TrimStart().StartsWith("<"))
            return default;

        return JsonConvert.DeserializeObject<T>(content);
    }

    private class NominatimPlace
    {
        public string lat { get; set; }
        public string lon { get; set; }
        public string display_name { get; set; }
    }

    // ===================== HTML peta Leaflet =====================
    private static string BuildLeafletHtml()
    {
        return @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no' />
<link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css' />
<script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
<style>html,body,#map{height:100%;margin:0;padding:0;} #map{background:#e8e8e8;}</style>
</head>
<body>
<div id='map'></div>
<script>
  var map = L.map('map', { zoomControl: true }).setView([-6.2088, 106.8456], 12);
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; OpenStreetMap'
  }).addTo(map);

  var marker = null;

  function notify(lat, lng) {
    window.location = 'app://pick?lat=' + lat + '&lng=' + lng;
  }

  function placeMarker(lat, lng) {
    if (marker) {
      marker.setLatLng([lat, lng]);
    } else {
      marker = L.marker([lat, lng], { draggable: true }).addTo(map);
      marker.on('dragend', function (ev) {
        var p = ev.target.getLatLng();
        notify(p.lat, p.lng);
      });
    }
  }

  // Dipanggil dari C# saat pilih kota / hasil pencarian / GPS.
  function setCity(lat, lng, zoom) {
    map.setView([lat, lng], zoom || 13);
    placeMarker(lat, lng);
  }

  map.on('click', function (e) {
    placeMarker(e.latlng.lat, e.latlng.lng);
    notify(e.latlng.lat, e.latlng.lng);
  });

  // Beri tahu C# bahwa peta sudah siap.
  setTimeout(function () { window.location = 'app://ready'; }, 300);
</script>
</body>
</html>";
    }

    private async void Tap_BGPS_Tapped(object sender, TappedEventArgs e)
    {
        await DetectGpsAsync(manual: true);
    }

    private async void TapCari_Tapped(object sender, TappedEventArgs e)
    {
        // ===================== Pencarian alamat / tempat =====================
   
        string q = SearchEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(q)) return;

        SetLoading(true);
        try
        {
            string url = $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&accept-language=id&q={Uri.EscapeDataString(q)}";
            var place = (await GetNominatimAsync<List<NominatimPlace>>(url))?.FirstOrDefault();

            if (place == null)
            {
                await DisplayAlertAsync("Tidak Ditemukan", "Lokasi tidak ditemukan. Coba kata kunci lain.", "OK");
                return;
            }

            double lat = double.Parse(place.lat, Inv);
            double lng = double.Parse(place.lon, Inv);
            _fromGps = false;
            await PindahPetaAsync(lat, lng, 16);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Gagal Mencari", ex.Message, "OK");
        }
        finally
        {
            SetLoading(false);
        }
    }

    
}
