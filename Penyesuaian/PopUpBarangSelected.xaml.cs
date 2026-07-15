using CommunityToolkit.Maui.Views;
using Newtonsoft.Json;
using System.Net.Http.Headers;
namespace MyPosAccurate2026.Penyesuaian;

public partial class PopUpBarangSelected : Popup
{
	string _noItem;
	public PopUpBarangSelected(string itemNo)
	{
		InitializeComponent();
		_noItem = itemNo;
	}
}