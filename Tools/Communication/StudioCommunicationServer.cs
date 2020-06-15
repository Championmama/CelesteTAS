﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
//using Microsoft.Xna.Framework.Input;


namespace CelesteStudio.Communication {
	public sealed class StudioCommunicationServer : StudioCommunicationBase {

		public static StudioCommunicationServer instance;

		private StudioCommunicationServer() {
		}

		public static void Run() {
			//this should be modified to check if there's another studio open as well
			if (instance != null)
				return;

			instance = new StudioCommunicationServer();

			ThreadStart mainLoop = new ThreadStart(instance.UpdateLoop);
			Thread updateThread = new Thread(mainLoop);
			updateThread.Name = "StudioCom Server";
			updateThread.IsBackground = true;
			updateThread.Start();
		}


		#region Read
		protected override void ReadData(Message message) {
			switch (message.ID) {
				case MessageIDs.EstablishConnection:
					throw new NeedsResetException("Recieved initialization message (EstablishConnection) from main loop");
				case MessageIDs.Reset:
					throw new NeedsResetException("Recieved reset message from main loop");
				case MessageIDs.Wait:
					ProcessWait();
					break;
				case MessageIDs.SendState:
					ProcessSendState(message.Data);
					break;
				case MessageIDs.SendPlayerData:
					ProcessSendPlayerData(message.Data);
					break;
				case MessageIDs.SendCurrentBindings:
					ProcessSendCurrentBindings(message.Data);
					break;
				//Spaghetti af, there's no good way of handling this.
				//The only way a send path can possibly end up here is if you restart Celeste then send a message through Studio.
				//The celeste side is already partially through initialization so we have to go to midway through initialization code.
				//EstablishConnection() takes care of the resyncing.
				//Can it potentially go through here twice in a row? Oh you bet it can go through here twice in a row.
				//But it works and frankly I'm terrified of fixing it.
				case MessageIDs.SendPath:
					//EstablishConnection();
					throw new NeedsResetException("Recieved initialization message (SendPath) from main loop");
				default:
					throw new InvalidOperationException($"{message.ID}");
			}
		}

		private void ProcessSendPath(byte[] data) {
			string path = Encoding.Default.GetString(data);
			Log(path);
			Wrapper.gamePath = path;
		}

		private void ProcessSendState(byte[] data) {
			string[] stateAndData = FromByteArray<string[]>(data);
			//Log(stateAndData[0]);
			Wrapper.state = stateAndData[0];
			Wrapper.playerData = stateAndData[1];
		}

		private void ProcessSendPlayerData(byte[] data) {
			string playerData = Encoding.Default.GetString(data);
			//Log(playerData);
			Wrapper.playerData = playerData;
		}

		private void ProcessSendCurrentBindings(byte[] data) {
			List<Keys>[] keys = FromByteArray<List<Keys>[]>(data);
			foreach (List<Keys> key in keys)
				Log(key.ToString());
			Wrapper.SetBindings(keys);
		}

		#endregion

		#region Write


		protected override void EstablishConnection() {
			var studio = this;
			var celeste = this;
			celeste = null;
			Message? lastMessage;


			studio?.ReadMessage();
			studio?.WriteMessageGuaranteed(new Message(MessageIDs.EstablishConnection, new byte[0]));
			celeste?.ReadMessageGuaranteed();

			celeste?.SendPath(null, true);
			lastMessage = studio?.ReadMessageGuaranteed();
			if (lastMessage?.ID != MessageIDs.SendPath)
				throw new NeedsResetException("Invalid data recieved while establishing connection");
			studio?.ProcessSendPath(lastMessage?.Data);

			studio?.SendPathNow(Studio.instance.tasText.LastFileName, false);
			lastMessage = celeste?.ReadMessageGuaranteed();
			celeste?.ProcessSendPath(lastMessage?.Data);

			//celeste?.SendCurrentBindings(Hotkeys.listHotkeyKeys);
			lastMessage = studio?.ReadMessageGuaranteed();
			if (lastMessage?.ID != MessageIDs.SendCurrentBindings)
				throw new NeedsResetException("Invalid data recieved while establishing connection");
			studio?.ProcessSendCurrentBindings(lastMessage?.Data);

			Initialized = true;
		}

		public void SendPath(string path, bool canFail) {
			pendingWrite = () => SendPathNow(path, canFail);
		}

		private void SendPathNow(string path, bool canFail) {
			if (Initialized || !canFail) {
				byte[] pathBytes;
				if (path != null)
					pathBytes = Encoding.Default.GetBytes(path);
				else
					pathBytes = new byte[0];
				WriteMessageGuaranteed(new Message(MessageIDs.SendPath, pathBytes));
			}
		}

		public void SendHotkeyPressed(HotkeyIDs hotkey) {
			pendingWrite = () => SendHotkeyPressedNow(hotkey);
		}

		private void SendHotkeyPressedNow(HotkeyIDs hotkey) {
			if (!Initialized)
				return;
			byte[] hotkeyByte = new byte[] { (byte)hotkey };
			WriteMessageGuaranteed(new Message(MessageIDs.SendHotkeyPressed, hotkeyByte));
		}

		private void SendNewBindings(List<Keys> keys) {
			byte[] data = ToByteArray(keys);
			WriteMessageGuaranteed(new Message(MessageIDs.SendNewBindings, data));
		}

		private void SendReloadBindings(byte[] data) {
			WriteMessageGuaranteed(new Message(MessageIDs.ReloadBindings, new byte[0]));
		}

		#endregion
	}
}
