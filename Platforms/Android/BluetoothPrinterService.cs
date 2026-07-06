using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.Bluetooth;
using Java.Util;

namespace MyPosAccurate2026.Platforms.Android
{
    public class BluetoothPrinterService
    {
        private static readonly UUID SPP_UUID = UUID.FromString("00001101-0000-1000-8000-00805f9b34fb");
        private BluetoothSocket _socket;
        private BluetoothDevice _device;

        public async Task<bool> ConnectToPrinterAsync(string deviceName)
        {
            try
            {
                BluetoothAdapter bluetoothAdapter = BluetoothAdapter.DefaultAdapter;
                if (bluetoothAdapter == null)
                {
                    Debug.WriteLine("Bluetooth tidak tersedia.");
                    return false;
                }

                if (!bluetoothAdapter.IsEnabled)
                {
                    Debug.WriteLine("Bluetooth belum diaktifkan.");
                    return false;
                }

                _device = bluetoothAdapter.BondedDevices.FirstOrDefault(d => d.Name == deviceName);
                if (_device == null)
                {
                    Debug.WriteLine($"Perangkat {deviceName} tidak ditemukan.");
                    return false;
                }

                // **Tutup koneksi lama sebelum membuat koneksi baru**
                if (_socket != null && _socket.IsConnected)
                {
                    _socket.Close();
                    _socket = null;
                    Debug.WriteLine("Koneksi lama ditutup.");
                }

                // Buat koneksi baru
                _socket = _device.CreateRfcommSocketToServiceRecord(SPP_UUID);
                await _socket.ConnectAsync();

                Debug.WriteLine("Terhubung ke printer!");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Gagal terhubung ke printer: {ex.Message}");
                return false;
            }
        }


        public async Task PrintAsync(string text)
        {
            if (_socket == null || !_socket.IsConnected)
            {
                Debug.WriteLine("Printer belum terhubung.");
                return;
            }

            try
            {
                byte[] buffer = Encoding.GetEncoding(437).GetBytes(text + "\n\n\n");

                // Kirim data dalam potongan kecil (512 byte per kirim)
                for (int i = 0; i < buffer.Length; i += 512)
                {
                    int size = Math.Min(512, buffer.Length - i);
                    _socket.OutputStream.Write(buffer, i, size);
                    await Task.Delay(50);  // Delay untuk mencegah buffer overflow pada printer
                }

                _socket.OutputStream.Flush();
                Debug.WriteLine("Struk berhasil dikirim ke printer.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Gagal mencetak: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            if (_socket != null)
            {
                _socket.Close();
                _socket = null;
                Debug.WriteLine("Koneksi ke printer ditutup.");
            }
        }
    }
}
