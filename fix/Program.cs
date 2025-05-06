using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;

namespace MinecraftAutoMiner
{
	static class Program
	{
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MinecraftAutoMinerForm());
		}
	}

	// Token: 0x02000002 RID: 2
	[SupportedOSPlatform("windows")]
	public class MinecraftAutoMinerForm : Form
	{
		// Token: 0x06000001 RID: 1
		[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "RegisterHotKey", SetLastError = true)]
		private static extern bool Win32RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

		// Token: 0x06000002 RID: 2
		[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "UnregisterHotKey", SetLastError = true)]
		private static extern bool Win32UnregisterHotKey(IntPtr hWnd, int id);

		// Token: 0x06000003 RID: 3
		[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "mouse_event")]
		private static extern void Win32MouseEvent(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

		// Token: 0x06000004 RID: 4
		[DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "keybd_event")]
		private static extern void Win32KeybdEvent(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

		// Token: 0x06000005 RID: 5 RVA: 0x00002050 File Offset: 0x00000250
		public MinecraftAutoMinerForm()
		{
			this._isMiningActive = false;
			this._miningTimer = new System.Windows.Forms.Timer
			{
				Interval = 100
			};
			this._miningTimer.Tick += this.MiningTick;
			this._fixTimer = new System.Windows.Forms.Timer
			{
				Interval = 60000
			};
			this._fixTimer.Tick += this.FixTick;
			base.SuspendLayout();
			this.Text = "Minecraft Auto-Miner";
			base.ClientSize = new Size(400, 200);
			base.StartPosition = FormStartPosition.CenterScreen;
			base.FormBorderStyle = FormBorderStyle.FixedSingle;
			base.MaximizeBox = false;
			this._mainButton = new Button
			{
				Text = "Start Mining (F1)",
				Size = new Size(200, 50),
				Location = new Point(100, 75),
				Font = new Font("Arial", 12f, FontStyle.Bold),
				BackColor = Color.LightGray
			};
			this._mainButton.Click += this.ToggleMining;
			base.Controls.Add(this._mainButton);
			base.ResumeLayout(false);
			this.RegisterHotkey();
		}

		// Token: 0x06000006 RID: 6 RVA: 0x000021AC File Offset: 0x000003AC
		private void RegisterHotkey()
		{
			try
			{
				bool flag = !MinecraftAutoMinerForm.Win32RegisterHotKey(base.Handle, this._hotkeyId, 0U, 112U);
				if (flag)
				{
					MessageBox.Show("Failed to register F1 hotkey. Please run as Administrator.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Error registering hotkey: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}

		// Token: 0x06000007 RID: 7 RVA: 0x00002224 File Offset: 0x00000424
		private void MiningTick(object? sender, EventArgs e)
		{
			bool flag = this._isMiningActive && !this._isMouseButtonDown;
			if (flag)
			{
				MinecraftAutoMinerForm.Win32MouseEvent(2U, 0U, 0U, 0U, 0);
				this._isMouseButtonDown = true;
			}
		}

		// Token: 0x06000008 RID: 8 RVA: 0x00002260 File Offset: 0x00000460
		private void FixTick(object? sender, EventArgs e)
		{
			bool isMiningActive = this._isMiningActive;
			if (isMiningActive)
			{
				this._miningTimer.Stop();
				bool isMouseButtonDown = this._isMouseButtonDown;
				if (isMouseButtonDown)
				{
					MinecraftAutoMinerForm.Win32MouseEvent(4U, 0U, 0U, 0U, 0);
					this._isMouseButtonDown = false;
				}
				Thread.Sleep(500);
				MinecraftAutoMinerForm.Win32KeybdEvent(84, 0, 0U, 0);
				Thread.Sleep(30);
				MinecraftAutoMinerForm.Win32KeybdEvent(84, 0, 2U, 0);
				Thread.Sleep(500);
				SendKeys.SendWait("/fix");
				Thread.Sleep(50);
				MinecraftAutoMinerForm.Win32KeybdEvent(13, 0, 0U, 0);
				Thread.Sleep(30);
				MinecraftAutoMinerForm.Win32KeybdEvent(13, 0, 2U, 0);
				Thread.Sleep(1000);
				MinecraftAutoMinerForm.Win32KeybdEvent(38, 0, 0U, 0);
				Thread.Sleep(30);
				MinecraftAutoMinerForm.Win32KeybdEvent(38, 0, 2U, 0);
				Thread.Sleep(50);
				MinecraftAutoMinerForm.Win32KeybdEvent(13, 0, 0U, 0);
				Thread.Sleep(30);
				MinecraftAutoMinerForm.Win32KeybdEvent(13, 0, 2U, 0);
				Thread.Sleep(2000);
				bool isMiningActive2 = this._isMiningActive;
				if (isMiningActive2)
				{
					bool isMouseButtonDown2 = this._isMouseButtonDown;
					if (isMouseButtonDown2)
					{
						MinecraftAutoMinerForm.Win32MouseEvent(4U, 0U, 0U, 0U, 0);
						this._isMouseButtonDown = false;
					}
					Thread.Sleep(100);
					this._miningTimer.Start();
					Thread.Sleep(100);
					MinecraftAutoMinerForm.Win32MouseEvent(2U, 0U, 0U, 0U, 0);
					this._isMouseButtonDown = true;
				}
			}
		}

		// Token: 0x06000009 RID: 9 RVA: 0x000023C0 File Offset: 0x000005C0
		private void ToggleMining(object? sender, EventArgs e)
		{
			this._isMiningActive = !this._isMiningActive;
			bool isMiningActive = this._isMiningActive;
			if (isMiningActive)
			{
				this.StartMining();
			}
			else
			{
				this.StopMining();
			}
		}

		// Token: 0x0600000A RID: 10 RVA: 0x000023FB File Offset: 0x000005FB
		private void StartMining()
		{
			this._miningTimer.Start();
			this._fixTimer.Start();
			this._mainButton.Text = "Stop Mining (F1)";
			this._mainButton.BackColor = Color.LightCoral;
		}

		// Token: 0x0600000B RID: 11 RVA: 0x00002438 File Offset: 0x00000638
		private void StopMining()
		{
			this._miningTimer.Stop();
			this._fixTimer.Stop();
			this._mainButton.Text = "Start Mining (F1)";
			this._mainButton.BackColor = Color.LightGray;
			bool isMouseButtonDown = this._isMouseButtonDown;
			if (isMouseButtonDown)
			{
				MinecraftAutoMinerForm.Win32MouseEvent(4U, 0U, 0U, 0U, 0);
				this._isMouseButtonDown = false;
			}
		}

		// Token: 0x0600000C RID: 12 RVA: 0x000024A0 File Offset: 0x000006A0
		protected override void WndProc(ref Message m)
		{
			bool flag = m.Msg == 786 && m.WParam.ToInt32() == this._hotkeyId;
			if (flag)
			{
				this.ToggleMining(this, EventArgs.Empty);
			}
			base.WndProc(ref m);
		}

		// Token: 0x0600000D RID: 13 RVA: 0x000024F0 File Offset: 0x000006F0
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				MinecraftAutoMinerForm.Win32UnregisterHotKey(base.Handle, this._hotkeyId);
				System.Windows.Forms.Timer miningTimer = this._miningTimer;
				if (miningTimer != null)
				{
					miningTimer.Dispose();
				}
				System.Windows.Forms.Timer fixTimer = this._fixTimer;
				if (fixTimer != null)
				{
					fixTimer.Dispose();
				}
			}
			base.Dispose(disposing);
		}

		// Token: 0x04000001 RID: 1
		private const uint MOUSE_LEFT_BUTTON_DOWN = 2U;

		// Token: 0x04000002 RID: 2
		private const uint MOUSE_LEFT_BUTTON_UP = 4U;

		// Token: 0x04000003 RID: 3
		private const int WM_HOTKEY_MESSAGE = 786;

		// Token: 0x04000004 RID: 4
		private const uint HOTKEY_NO_MODIFIER = 0U;

		// Token: 0x04000005 RID: 5
		private const uint VIRTUAL_KEY_F1 = 112U;

		// Token: 0x04000006 RID: 6
		private const byte VK_T = 84;

		// Token: 0x04000007 RID: 7
		private const byte VK_ENTER = 13;

		// Token: 0x04000008 RID: 8
		private const uint KEYEVENTF_KEYDOWN = 0U;

		// Token: 0x04000009 RID: 9
		private const uint KEYEVENTF_KEYUP = 2U;

		// Token: 0x0400000A RID: 10
		private readonly System.Windows.Forms.Timer _miningTimer;

		// Token: 0x0400000B RID: 11
		private readonly System.Windows.Forms.Timer _fixTimer;

		// Token: 0x0400000C RID: 12
		private readonly Button _mainButton;

		// Token: 0x0400000D RID: 13
		private bool _isMiningActive;

		// Token: 0x0400000E RID: 14
		private readonly int _hotkeyId = 1;

		// Token: 0x0400000F RID: 15
		private bool _isMouseButtonDown = false;
	}
}
