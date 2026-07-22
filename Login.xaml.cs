using Newtonsoft.Json;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Numerics;
using System.Text;


namespace MyPosAccurate2026;

public partial class Login : ContentPage
{
    public Login()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (Preferences.ContainsKey("SavedEmail") && Preferences.ContainsKey("SavedPassword"))
        {
            EmailEntry.Text = Preferences.Get("SavedEmail", "");
            PasswordEntry.Text = Preferences.Get("SavedPassword", "");
            RememberMeCheckBox.IsChecked = true;
        }
    }

    private void TogglePasswordBtn_Clicked(object sender, EventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        TogglePasswordBtn.Text = PasswordEntry.IsPassword ? "Show" : "Hide";
    }

    private void RememberMeLabel_Tapped(object sender, EventArgs e)
    {
        RememberMeCheckBox.IsChecked = !RememberMeCheckBox.IsChecked;
    }

    public class LoginResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public int code { get; set; }
        public UserData data { get; set; }
    }

    public class UserData
    {
        public int id_users { get; set; }
        public string username { get; set; }
        public string nama_lengkap { get; set; }
        public string token_key { get; set; }
        public string valid { get; set; }
    }

    private async void B_Login_Clicked(object sender, EventArgs e)
    {

        if (sender is Button image)
        {
            await image.FadeToAsync(0.3, 100); // Turunkan opacity ke 0.3 dalam 100ms
            await image.FadeToAsync(1, 200);   // Kembalikan opacity ke 1 dalam 200ms
        }


        string email = EmailEntry.Text?.Trim();
        string password = PasswordEntry.Text;


        if (string.IsNullOrWhiteSpace(email))
        {
            await DisplayAlertAsync("Validasi", "Alamat email harus diisi.", "OK");
            EmailEntry.Focus();
            return;
        }


        if (string.IsNullOrWhiteSpace(password))
        {
            await DisplayAlertAsync("Validasi", "Password harus diisi.", "OK");
            PasswordEntry.Focus();
            return;
        }


        if (!IsValidEmail(email))
        {
            await DisplayAlertAsync("Validasi", "Format alamat email tidak valid.", "OK");
            EmailEntry.Focus();
            return;
        }


        await ProcessLoginAsync(email, password);
    }


    private bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {

            return System.Text.RegularExpressions.Regex.IsMatch(
                email,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(250)); // Timeout cegah ReDoS
        }
        catch
        {
            return false;
        }
    }


    private async Task ProcessLoginAsync(string email, string password)
    {

        B_Login.IsEnabled = false;
        B_Login.Text = "Memproses...";

        try
        {


            await Task.Delay(1000);


            cek_login();


        }
        catch (Exception ex)
        {

            await DisplayAlertAsync("Error", $"Gagal login: {ex.Message}", "OK");
        }
        finally
        {

            B_Login.IsEnabled = true;
            B_Login.Text = "Login";
        }
    }



    private async void cek_login()
    {
        var requestData = new Dictionary<string, string>
    {
        { "email", EmailEntry.Text?.Trim() },
        { "password", PasswordEntry.Text }
    };

        var jsonData = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
        var client = new HttpClient();
        string ip = App.API_LOGIN;

        try
        {
            var response = await client.PostAsync(ip, jsonData);

            // 1. Baca balasan mentah (berupa string) dari server
            var responseContent = await response.Content.ReadAsStringAsync();

            

            // 3. Cegat jika server malah mengembalikan HTML/Error PHP (Berawalan '<')
            if (responseContent.StartsWith("<"))
            {
                await DisplayAlertAsync("Error Server (PHP)", "Server tidak mengembalikan JSON. Cek Output/Debug Console di Visual Studio untuk melihat isi error aslinya.", "OK");
                return;
            }

            // 4. Jika aman, lakukan Deserialize ke Class Model
            var responseObject = JsonConvert.DeserializeObject<LoginResponse>(responseContent);

            if (responseObject != null && responseObject.status == "success")
            {
                System.Diagnostics.Debug.WriteLine($"Login Sukses API: {ip}");
                System.Diagnostics.Debug.WriteLine($"id_users: {responseObject.data.id_users}");

                // Simpan data ke Preferences
                Preferences.Set("ID_USER", responseObject.data.id_users.ToString());
                Preferences.Set("USERNAME", responseObject.data.username);
                Preferences.Set("NAMA_LENGKAP", responseObject.data.nama_lengkap);
                Preferences.Set("TOKEN_KEY", responseObject.data.token_key);

                if (RememberMeCheckBox.IsChecked)
                {
                    Preferences.Set("SavedEmail", EmailEntry.Text?.Trim());
                    Preferences.Set("SavedPassword", PasswordEntry.Text);
                }
                else
                {
                    Preferences.Remove("SavedEmail");
                    Preferences.Remove("SavedPassword");
                }
               
                
                Application.Current.MainPage = new NavigationPage(new Beranda());
            }
            else
            {
                await Task.Delay(1000);
                string errorMsg = responseObject?.message ?? "Terjadi kesalahan saat login.";
                await DisplayAlertAsync("Informasi Login", errorMsg, "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Koneksi Gagal", ex.Message, "OK");
        }
    }
}