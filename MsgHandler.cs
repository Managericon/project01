using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public class MsgHandler
{
    public static void MsgPing(ClientState c, MsgBase msgBase)
    {
        Console.WriteLine("MsgPing:" + c.socket.RemoteEndPoint);
        c.lastPingTime = Gateway.GetTimeStamp();
        MsgPong msgPong = new MsgPong();
        Gateway.Send(c, msgPong);
    }
}

