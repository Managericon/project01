using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#nullable disable
public class MainClass
{
    public static ServerState fighterServer;
    public static void Main()
    {
        fighterServer = Gateway.ConnectServer("127.0.0.1", 9000);
        Gateway.type2ss.Add(Gateway.ServerType.Fighter, fighterServer);

        Gateway.Connect("127.0.0.1", 8888);
    }

}

