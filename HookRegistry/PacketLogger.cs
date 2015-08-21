using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;

namespace Hooks
{
    /*
     *  Watch the packets in real time like this:
     *      $ tail -f /cygdrive/s/Program\ Files\ \(x86\)/Hearthstone/Hearthstone_Data/output_log.txt | grep --line-buffered "####" | cut -c 6-
     * 
     *  Export to a file
     *      $ cat /cygdrive/s/Program\ Files\ \(x86\)/Hearthstone/Hearthstone_Data/output_log.txt | grep "####" | cut -c 6- > packets.txt
     * 
     */



    [RuntimeHookAttribute]
    public class PacketLogger
    {
        public PacketLogger()
        {
            HookRegistry.Register(OnCall);
        }

        public static void Send(bool request, string payload)
        {
            return; // TODO
            new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.IsBackground = true;

                    var httpWebRequest = (HttpWebRequest)WebRequest.Create("http://tyr:8000/" + (request ? "request" : "response"));
                    httpWebRequest.ContentType = "text/plain";
                    httpWebRequest.Method = "POST";
                    var stream = httpWebRequest.GetRequestStream();
                    using (var streamWriter = new StreamWriter(stream))
                    {
                        streamWriter.Write(payload);
                    }

                    var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                    using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                    {
                        string result = streamReader.ReadToEnd();
                        Log.Bob.Print("Response to posting: " + result);
                    }
                }
                catch (System.Exception e)
                {
                    Log.Bob.Print(e.Message);
                }

            }).Start();
        }

        private static string ValueToStr(object o, int depth=1)
        {
            StringBuilder sb = new StringBuilder();
            if (o is IProtoBuf){
                sb.Append("{\n#### ");
                foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties((IProtoBuf)o))
                {
                    for (int i=0;i<depth;i++)
                       sb.Append("  ");

                    string name = descriptor.Name;
                    object v = descriptor.GetValue(o);
                    sb.Append("\"" + name + "\" : ");
                    sb.Append(ValueToStr(v, depth+1));
                    sb.Append(",\n#### ");
                }
                for (int i = 0; i < depth-1; i++)
                    sb.Append("  ");
                sb.Append("}");
            }
            else if (o is IList)
            {
                sb.Append("[\n#### ");
                foreach (object e in ((IList)o))
                {
                    for (int i = 0; i < depth; i++)
                        sb.Append("  ");
                    sb.Append(ValueToStr(e, depth + 1));
                    sb.Append(",\n#### ");
                }
                for (int i = 0; i < depth - 1; i++)
                    sb.Append("  ");
                sb.Append("]");
            }
            else if (o == null)
            {
                return "None";
            }
            else
            {
                return o.ToString();
            }

            return sb.ToString();
        }

        object OnCall(string typeName, string methodName, object thisObj, params object[] args)
        {

            if (methodName == "UtilOutbound")
            {
                IProtoBuf body = (IProtoBuf)args[2];
                Send(true, "(" + body.ToString() + ", " + ValueToStr(body));
                Log.Bob.Print("\n#### Request: " + body.ToString() + "\n#### " + ValueToStr(body) + "\n#### ");
                return null;
            }
            else if (methodName == "NextUtilPacket")
            {
                Type type = typeof(BattleNet);
                FieldInfo info = type.GetField("s_impl", BindingFlags.NonPublic | BindingFlags.Static);
                object value = info.GetValue(null);
                IBattleNet s_impl = (IBattleNet)value;
                PegasusPacket packet = s_impl.NextUtilPacket();
                if (packet != null && packet.Body is byte[])
                {
                    type = typeof(ConnectAPI);
                    info = type.GetField("s_packetDecoders", BindingFlags.NonPublic | BindingFlags.Static);
                    value = info.GetValue(null);
                    SortedDictionary<int, ConnectAPI.PacketDecoder> s_packetDecoders = (SortedDictionary<int, ConnectAPI.PacketDecoder>)value;

                    PegasusPacket packetCopy = new PegasusPacket(packet.Type, packet.Context, packet.Size, packet.Body);
                    ConnectAPI.PacketDecoder packetDecoder;
                    if (s_packetDecoders.TryGetValue(packetCopy.Type, out packetDecoder))
                    {
                        PegasusPacket decodedPacket = packetDecoder.HandlePacket(packetCopy);
                        if (decodedPacket != null)
                        {
                            IProtoBuf body = (IProtoBuf)(decodedPacket.Body);
                            Send(false, "(" + body.ToString() + ", " + ValueToStr(body));
                            Log.Bob.Print("\n#### Response: " + body.ToString() + "\n#### " + ValueToStr(body) + "\n#### ");
                        }
                    }
                    return packet;
                }                   
            }
            return null;
        }
    }
}