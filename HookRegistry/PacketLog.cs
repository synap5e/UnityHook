using Newtonsoft.Json;
using PegasusGame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using System.Text;
using System.IO;
using bgs;

namespace Hooks
{

	[RuntimeHook]
	class PacketLog
	{
		public PacketLog()
		{
			HookRegistry.Register(OnCall);
		}

		object OnCall(string typeName, string methodName, object thisObj, params object[] args)
		{
			if (typeName == "ConnectAPI" && methodName == "QueueGamePacket")
			{
				int packetID = (int)args[0];
				IProtoBuf body = (IProtoBuf)args[1];
				return ConnectAPI_QueueGamePacket(packetID, body);
			}
			if (typeName == "ConnectAPI" && methodName == "PacketReceived")
			{
				PegasusPacket p = (PegasusPacket)args[0];
				Queue<PegasusPacket> state = (Queue<PegasusPacket>)args[1];
				return ConnectAPI_PacketReceived(p, state);
			}
			else if (typeName == "bgs.RPCConnection" && methodName == "QueuePacket")
			{
				RPCConnection thiz = (RPCConnection)thisObj;
				BattleNetPacket packet = (BattleNetPacket)args[0];
				return RPCConnection_QueuePacket(thiz, packet);
			}
			else if (typeName == "bgs.RPCConnection" && methodName == "PacketReceived")
			{
				RPCConnection thiz = (RPCConnection)thisObj;
				BattleNetPacket packet = (BattleNetPacket)args[0];
				return RPCConnection_PacketReceived(thiz, packet);
			}

			return null;
		}
		private object RPCConnection_PacketReceived(RPCConnection thisObj, BattleNetPacket packet)
		{
			using (StreamWriter sw = new StreamWriter("rpc_packets.txt", true))
			{
				object body = packet.GetBody();
				MethodDescriptor.ParseMethod parseMethod = null;
				string serviceDescriptor = "";

				if (packet.GetHeader().ServiceId == 254)
				{
					RPCContext rPCContext;
					var waitingForResponse = typeof(RPCConnection).GetField("waitingForResponse", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(thisObj) as Dictionary<uint, RPCContext>;
					if (waitingForResponse.TryGetValue(packet.GetHeader().Token, out rPCContext))
					{
						ServiceDescriptor importedServiceById = thisObj.serviceHelper.GetImportedServiceById(rPCContext.Header.ServiceId);
						if (importedServiceById != null)
						{
							parseMethod = importedServiceById.GetParser(rPCContext.Header.MethodId);
							body = parseMethod((byte[])body);
						}
					}
				}
				else
				{
					MethodInfo dynMethod = typeof(RPCConnection).GetMethod("GetExportedServiceDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
					ServiceDescriptor exportedServiceDescriptor = (ServiceDescriptor)dynMethod.Invoke(thisObj, new object[] { packet.GetHeader().ServiceId });
					serviceDescriptor = exportedServiceDescriptor.ToString();

					if (exportedServiceDescriptor != null)
					{
						parseMethod = thisObj.serviceHelper.GetExportedServiceById(packet.GetHeader().ServiceId).GetParser(packet.GetHeader().MethodId);
						//   sw.WriteLine("parseMethod: " + parseMethod);
						if (parseMethod != null)
						{
							body = parseMethod((byte[])body);
						}
					}
					//ServiceDescriptor exportedServiceDescriptor = thisObj.GetExportedServiceDescriptor();
				}



				sw.WriteLine("{\n" +
					" \"method\": \"RPCConnection.PacketReceived\",\n" +
					" \"serviceDescriptor\": \"" + serviceDescriptor + "\",\n" +
					" \"type\": \"" + body.ToString() + "\",\n" +
					" \"header\": " + JsonConvert.SerializeObject(packet.GetHeader(), Formatting.Indented).Replace("\n", "\n ") + ",\n" +
					" \"body\": " + JsonConvert.SerializeObject(body, Formatting.Indented).Replace("\n", "\n ") + ",\n" +
					" \"parseMethod\": " + (parseMethod != null ? "\"" + parseMethod.ToString() + "\"" : "null") + "\n" +
				 "}");
			}
			return null;
		}

		private object RPCConnection_QueuePacket(RPCConnection thisObj, BattleNetPacket packet)
		{
			using (StreamWriter sw = new StreamWriter("rpc_packets.txt", true))
			{
				sw.WriteLine("{\n" +
					" \"method\": \"RPCConnection.QueuePacket\",\n" +
					" \"type\": \"" + packet.GetBody().ToString() + "\",\n" +
					" \"header\": " + JsonConvert.SerializeObject(packet.GetHeader(), Formatting.Indented).Replace("\n", "\n ") + ",\n" +
					" \"body\": " + JsonConvert.SerializeObject(packet.GetBody(), Formatting.Indented).Replace("\n", "\n ") + "\n" +
				 "}");
			}
			return null;
		}


		private object ConnectAPI_PacketReceived(PegasusPacket p, Queue<PegasusPacket> queue)
		{
			using (StreamWriter sw = new StreamWriter("packets.txt", true))
			{
				var s_packetDecoders = typeof(ConnectAPI).GetField("s_packetDecoders", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as SortedDictionary<int, ConnectAPI.PacketDecoder>;
				ConnectAPI.PacketDecoder packetDecoder;
				PegasusPacket decoded = null;
				if (s_packetDecoders.TryGetValue(p.Type, out packetDecoder))
				{

					//sw.WriteLine(packetDecoder);
					PegasusPacket decode = new PegasusPacket(p.Type, p.Context, p.Size, p.Body);
					decoded = packetDecoder.HandlePacket(decode);

					using (StreamWriter sw2 = new StreamWriter("PowerHistory.txt", true))
					{
						if (packetDecoder.ToString().Contains("PowerHistory"))
						{
							sw2.Write("{\n" +
										"\"method\": \"ConnectAPI.PacketReceived\",\n" +
										"\"type\": " + p.Type + ",\n" +
										"\"decoder\": \"" + packetDecoder + "\",\n" +
										"\"body\": " + JsonConvert.SerializeObject(decoded.Body, Formatting.Indented) + "\n" +
									"}\n");
						}
					}

					if (packetDecoder.ToString().Contains("PowerHistory") || packetDecoder.ToString().Contains("Pong")) return null;

					sw.Write("{\n" +
					   "\"method\": \"ConnectAPI.PacketReceived\",\n" +
					   "\"type\": " + p.Type + ",\n" +
					   "\"decoder\": \"" + packetDecoder + "\",\n"
					);
					if (decoded == null)
					{
						sw.Write("\"body\": null\n");
					}
					else
					{
						sw.Write("\"body\": " + JsonConvert.SerializeObject(decoded.Body, Formatting.Indented) + "\n");
					}
					sw.Write("}\n");
				}
				else
				{
					sw.WriteLine("{\n" +
					   "\"method\": \"ConnectAPI.PacketReceived\",\n" +
					   "\"type\": " + p.Type + ",\n" +
					   "\"decoder\": null,\n" +
					   "\"dump\": " + JsonConvert.SerializeObject(p, Formatting.Indented) + "\n" +
					 "}");
				}
			}
			return null;
		}



		private object ConnectAPI_QueueGamePacket(int packetID, IProtoBuf body)
		{
			using (StreamWriter sw = new StreamWriter("packets.txt", true))
			{

				//if (packetID == 115) return null; // ping

				/*if (packetID == 15 && !allow)
				{
					return "";
				}*/

				sw.WriteLine("{\n" +
								"\"method\": \"ConnectAPI.QueueGamePacket\",\n" +
								"\"type\": " + packetID + ",\n" +
								"\"packet\": " + JsonConvert.SerializeObject(body, Formatting.Indented) + "\n" +
							 "}");


			}
			return null;
		}
	}
}
