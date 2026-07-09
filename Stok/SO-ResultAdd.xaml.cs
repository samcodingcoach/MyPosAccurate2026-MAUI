namespace MyPosAccurate2026.Stok;

public partial class SO_ResultAdd : ContentPage
{
	public SO_ResultAdd()
	{
		InitializeComponent();
	}

    private void HiddenDatePicker_DateSelected(object sender, DateChangedEventArgs e)
    {
        FormStartDate.Text = $"{e.NewDate:dd/MM/yyyy}";
    }
}