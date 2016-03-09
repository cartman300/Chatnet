using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Reflection;

using Libraria.Net;

namespace Chatnet {
	public class Program {
		static string ServerCol = "34m";

		static void Main(string[] args) {
			Console.Title = "Chatnet Telnet Server";

#if !DEBUG
			while (true) {
				try {
#endif
			TelnetServer TS = new TelnetServer(6666);
			TS.OnConnected += (TC) => new Thread(() => OnClient(TC, TS)).Start();
			TS.OnAliasChanged += (TC, Old, New) => TS.InsertLine(TStamp() + Colored(ServerCol, "{0} changed alias to {1}"), Old, New);
			TS.OnWrite += (Str) => {
				try {
					while (Str.Contains((char)27)) {
						int EscStart = Str.IndexOf((char)27);
						Str = Str.Remove(EscStart, Str.IndexOf('m', EscStart) - EscStart + 1);
					}
					Str = Str.Replace("\a", "");
				} catch (Exception) {
				}
				Console.Write(Str);
			};
			Console.WriteLine("Starting telnet server");
			TS.Run();
#if !DEBUG
				} catch (Exception E) {
					File.AppendAllText("Exceptions.txt", E.ToString());
				}
			}
#endif

			Console.WriteLine("Done!");
			Console.ReadLine();
		}

		public static string Esc(string E) {
			return "\x1B[" + E;
		}

		public static string Escd(string E, params string[] Escs) {
			foreach (var EE in Escs)
				E = Esc(EE) + E;
			return E + Esc("0m");
		}

		public static string Colored(string C, string Str) {
			return Esc(C) + Esc("40m") + Str + Esc("0m");
		}

		public static string TStamp() {
			return Escd("[" + DateTime.Now.ToString("HH:mm:ss") + "] ", "37m");
		}

		static void OnClient(TelnetClient Client, TelnetServer Server) {
#if !DEBUG
			try {
#endif
			Server.InsertLine(TStamp() + Colored(ServerCol, "Client connected: {0}"), Client);

			string Alias;
		EnterAlias:
			Alias = Client.ReadLine("Username: ").Trim();
			if (Alias.Length == 0 || Alias.Length > 25) {
				Client.InsertLine("Invalid username");
				goto EnterAlias;
			}
			lock (Server.Clients)
			foreach (var Cl in Server.Clients)
					if (Cl.Alias == Alias) {
						Client.InsertLine("Username '{0}' already taken, try again", Alias);
						goto EnterAlias;
					}
			Client.Alias = Alias;

			DateTime Last = DateTime.Now;
			int Warnings = 0;

			while (Client.Connected) {
				string Input = Client.ReadLine(Escd(Client.ToString(), "1m", "32m") + Escd(" $ ", "1m", "32m"), true).Trim();
				if (Input.Length == 0) {
					Client.Write('\r');
					continue;
				}

				if (Input.StartsWith("/")) {
					string[] Cmd = Input.Substring(1).Split(' ');

					if (Cmd.Length > 0) {
						MethodInfo MI = typeof(Commands).GetMethod(Cmd[0],
							new Type[] { typeof(TelnetClient), typeof(TelnetServer), typeof(string[]), typeof(string) });
						Client.WriteLine("");

						if (MI != null)
							MI.Invoke(null, new object[] { Client, Server, Cmd, Input });
						else
							Client.InsertLine(Escd("Unknown command '{0}'", "31m"), Cmd[0]);
					}
					continue;
				}

				if ((DateTime.Now - Last).Milliseconds < 100)
					Warnings++;

				if (Warnings > 5) {
					Server.InsertLine(TStamp() + Colored(ServerCol, "Kicked for flooding: {0}"), Client);
					Client.Disconnect();
					continue;
				}

				Last = DateTime.Now;
				Server.InsertLine(Say(Client.ToString(), Input));
			}
#if !DEBUG
			} catch (Exception) {
			}
#endif
			if (Client.Connected)
				Client.Disconnect();
			Server.InsertLine(TStamp() + Colored(ServerCol, "Client disconnected: {0}"), Client);
		}

		public static string Say(string Client, string Input) {
			return string.Format(TStamp() + Escd("\a{0}: {1}", "36m"), Client, Input);
		}
	}

	static class Commands {
		public static object help(TelnetClient Client, TelnetServer Server, string[] Args, string In) {
			if (Client == null)
				return "Returns all commands";

			MethodInfo[] Methods = typeof(Commands).GetMethods(BindingFlags.Public | BindingFlags.Static);
			Client.WriteLine("Commands:");
			for (int i = 0; i < Methods.Length; i++)
				Client.WriteLine("  /{0} - {1}", Methods[i].Name, Methods[i].Invoke(null, new object[] { null, null, null, null }));

			return null;
		}

		public static object quit(TelnetClient Client, TelnetServer Server, string[] Args, string In) {
			if (Client == null)
				return "Disconnects user";
			Client.Disconnect();

			return null;
		}

		public static object q(TelnetClient Client, TelnetServer Server, string[] Args, string In) {
			if (Client == null)
				return "Alias for quit";

			return quit(Client, Server, Args, In);
		}

		public static object me(TelnetClient Client, TelnetServer Server, string[] Args, string In) {
			if (Client == null)
				return "Similar to old steam /me";

			if (!In.Contains(' '))
				return null;
			Server.InsertLine(Program.TStamp() + Program.Escd("\a{0}{1}", "36m"), Client, In.Substring(In.IndexOf(' ')));

			return null;
		}

		public static object list(TelnetClient Client, TelnetServer Server, string[] Args, string In) {
			if (Client == null)
				return "Returns all online users";

			Client.InsertLine("Online users:");
			lock (Server.Clients) {
				foreach (var Cl in Server.Clients)
					Client.InsertLine("  " + Cl);
			}

			return null;
		}

		public static object colors(TelnetClient Client, TelnetServer Server, string[] Args, string In) {
			if (Client == null)
				return "Toggles escape sequences";

			Client.EscapeSequences = !Client.EscapeSequences;
			Client.InsertLine("Colors " + (Client.EscapeSequences ? "enabled" : "disabled"));

			return null;
		}

		public static object pm(TelnetClient Client, TelnetServer Server, string[] Args, string In) {
			if (Client == null)
				return "Send private message to user";

			if (Args.Length < 3)
				return null;
			TelnetClient PMClient = Server.FindClient(Args[1]);
			if (PMClient != null) {
				string Line = Program.Say(Client + " to " + PMClient, In.Substring(In.IndexOf(' ', In.IndexOf(' ') + 1) + 1));
				Client.InsertLine(Line);
				PMClient.InsertLine(Line);
			}
			return null;
		}
	}
}