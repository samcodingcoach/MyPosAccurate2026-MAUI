using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.Bluetooth;
using Java.Util;
using MyPosAccurate2026.Platforms.Android;
using MyPosAccurate2026;
[assembly: Dependency(typeof(AndroidBlueToothService))]
namespace MyPosAccurate2026.Platforms.Android
{
    public class AndroidBlueToothService : IBluetoothService
    {
        BluetoothAdapter bluetoothAdapter = BluetoothAdapter.DefaultAdapter;
        public IList<string> GetDeviceList()
        {
            var btdevice = bluetoothAdapter?.BondedDevices
                .Select(i => i.Name).ToList();
            return btdevice;
        }

        public async Task Print(string deviceName, string text)
        {
            BluetoothDevice device = (from bd in bluetoothAdapter?.BondedDevices where bd?.Name == deviceName select bd).FirstOrDefault();

            try
            {
                await Task.Delay(3000);
                BluetoothSocket bluetoothSocket = device?.CreateRfcommSocketToServiceRecord(UUID.FromString("00001101-0000-1000-8000-00805f9b34fb"));

                bluetoothSocket?.Connect();
                byte[] buffer = Encoding.UTF8.GetBytes(text);
                bluetoothSocket?.OutputStream.Write(buffer, 0, buffer.Length);
                bluetoothSocket.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                throw ex;
            }
        }
    }
}
