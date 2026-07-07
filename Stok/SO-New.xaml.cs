namespace MyPosAccurate2026.Stok;

public partial class SO_New : ContentPage
{
	public SO_New()
	{
		InitializeComponent();
        HiddenDatePicker.MinimumDate = DateTime.Today; // Kunci agar tidak boleh kurang dari sekarang
	}

    private void HiddenDatePicker_DateSelected(object sender, DateChangedEventArgs e)
    {
        FormStartDate.Text = $"{e.NewDate:dd/MM/yyyy}";
    }
}