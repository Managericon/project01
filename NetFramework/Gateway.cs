using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Channels;
#nullable disable
public static class Gateway
{
    public enum ServerType
    {
        Gateway,//网关服务器
        Fighter,//战斗服务器
    }
    /// <summary>
    /// 用于连接客户端的服务端socket
    /// </summary>
    public static Socket listenfd;
    /// <summary>
    /// 用于连接其他服务端的Socket
    /// </summary>
    public static Socket gateway;
    /// <summary>
    /// 客户端字典
    /// </summary>
    public static Dictionary<Socket, ClientState> clientStates = new Dictionary<Socket, ClientState>();
    /// <summary>
    /// 其他服务端字典
    /// </summary>
    public static Dictionary<Socket, ServerState> serverStates = new Dictionary<Socket, ServerState>();

    /// <summary>
    /// 服务器类型和服务器的映射
    /// </summary>
    public static Dictionary<ServerType, ServerState> type2ss = new Dictionary<ServerType, ServerState>();
    /// <summary>
    /// 通过id找到相应客户端的字典
    /// </summary>
    public static Dictionary<uint, ClientState> id2cs = new Dictionary<uint, ClientState>();
    /// <summary>
    /// 用于检测的列表
    /// </summary>
    public static List<Socket> sockets = new List<Socket>();
    private static float pingInterval = 2;

    /// <summary>
    /// 接收客户端的udp
    /// </summary>
    private static UdpClient receiveClientUdp;
    /// <summary>
    /// 接收服务端的udp
    /// </summary>
    private static UdpClient receiveServerUdp;
    /// <summary>
    /// 连接服务器
    /// </summary>
    /// <param name="ip">ip地址</param>
    /// <param name="port">端口号</param>
    public static void Connect(string ip, int port)
    {
        listenfd = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        IPAddress iPAddress = IPAddress.Parse(ip);
        IPEndPoint iPEndPoint = new IPEndPoint(iPAddress, port);
        listenfd.Bind(iPEndPoint);
        listenfd.Listen(0);


        receiveClientUdp = new UdpClient((IPEndPoint)listenfd.LocalEndPoint);

        Console.WriteLine("服务器启动成功");
        while (true)
        {
            sockets.Clear();
            //放服务端的socket
            sockets.Add(listenfd);
            //放客户端的Socket
            foreach (Socket socket in clientStates.Keys)
            {
                sockets.Add(socket);
            }
            Socket.Select(sockets, null, null, 1000);
            for (int i = 0; i < sockets.Count; i++)
            {
                Socket s = sockets[i];
                if (s == listenfd)
                {
                    //有客户端要连接
                    Accept(s);
                }
                else
                {
                    //客户端发消息过来了
                    Receive(s);
                }
            }
            //CheckPing();
        }
    }

    /// <summary>
    /// 连接其他服务器
    /// </summary>
    /// <param name="ip">ip地址</param>
    /// <param name="port">端口号</param>
    public static ServerState ConnectServer(string ip, int port)
    {
        ServerState serverState = new ServerState();
        gateway = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        IPAddress ipAddress = IPAddress.Parse(ip);
        IPEndPoint iPEndPoint = new IPEndPoint(ipAddress, port);
        gateway.Bind(iPEndPoint);
        gateway.Listen(0);
        Console.WriteLine("网关服务器等待其他服务器连接");
        gateway.BeginAccept(AcceptServerCallback, serverState);
        return serverState;
    }
    /// <summary>
    /// 接收其他服务端连接的回调
    /// </summary>
    /// <param name="ar"></param>
    private static void AcceptServerCallback(IAsyncResult ar)
    {
        //封装连接过来的服务端对象
        ServerState serverState = (ServerState)ar.AsyncState;
        Socket socket = gateway.EndAccept(ar);
        Console.WriteLine("连接成功");
        serverState.socket = socket;

        serverStates.Add(socket, serverState);


        //udp
        receiveServerUdp = new UdpClient((IPEndPoint)gateway.LocalEndPoint);
        receiveServerUdp.BeginReceive(ReceiveUdpServerCallback, serverState);

        //接收消息
        ByteArray byteArray = serverState.readBuffer;
        socket.BeginReceive(byteArray.bytes, byteArray.writeIndex, byteArray.Remain, 0, ReceiveServerCallback, serverState);
    }
    /// <summary>
    /// 接收其他服务端发过来的消息回调
    /// </summary>
    /// <param name="ar"></param>
    private static void ReceiveServerCallback(IAsyncResult ar)
    {
        ServerState serverState = (ServerState)ar.AsyncState;
        int count = 0;
        Socket server = serverState.socket;


        ByteArray byteArray = serverState.readBuffer;
        if (byteArray.Remain <= 0)
        {
            byteArray.MoveBytes();
        }
        if (byteArray.Remain <= 0)
        {
            Console.WriteLine("Receive fail :数组长度不足");
            //关闭服务端
            //Close();
            return;
        }
        try
        {
            count = server.EndReceive(ar);
        }
        catch (SocketException e)
        {
            Console.WriteLine("Receive fail:" + e.Message);
            //关闭服务端
            //Close();
            return;
        }
        if (count <= 0)
        {
            Console.WriteLine("Socket Close:" + serverState.socket.RemoteEndPoint.ToString());
            //关闭服务端
            //Close();
            return;
        }

        //处理接收过来的消息
        byteArray.writeIndex += count;
        OnReceiveData(serverState);
        byteArray.MoveBytes();

        server.BeginReceive(byteArray.bytes, byteArray.writeIndex, byteArray.Remain, 0, ReceiveServerCallback, serverState);
    }
    /// <summary>
    /// 处理服务端发过来的消息
    /// </summary>
    /// <param name="serverState"></param>
    private static void OnReceiveData(ServerState serverState)
    {
        ByteArray byteArray = serverState.readBuffer;
        byte[] bytes = byteArray.bytes;

        if (byteArray.Length <= 2)
        {
            return;
        }
        //解析长度
        short length = (short)(bytes[byteArray.readIndex + 1] * 256 + bytes[byteArray.readIndex]);

        if (byteArray.Length < length + 2)
        {
            return;
        }

        uint guid = (uint)(bytes[byteArray.readIndex + 2] << 24 |
                    bytes[byteArray.readIndex + 3] << 16 |
                    bytes[byteArray.readIndex + 4] << 8 |
                    bytes[byteArray.readIndex + 5]);
        byteArray.readIndex += 6;

        try
        {
            int msgLength = length - 4;
            //发送给客户端的数组
            byte[] sendBytes = new byte[msgLength + 2];
            //打包长度
            sendBytes[0] = (byte)(msgLength % 256);
            sendBytes[1] = (byte)(msgLength / 256);

            Array.Copy(bytes, byteArray.readIndex, sendBytes, 2, msgLength);


            id2cs[guid].socket.Send(sendBytes, 0);
        }
        catch (SocketException e)
        {
            Console.WriteLine(e.Message);
        }
        byteArray.readIndex += length - 4;

        //继续处理
        if (byteArray.Length > 2)
        {
            OnReceiveData(serverState);
        }
    }
    /// <summary>
    /// 接收客户端的连接
    /// </summary>
    /// <param name="listenfd">服务端的socket</param>
    private static void Accept(Socket listenfd)
    {
        try
        {
            Socket socket = listenfd.Accept();
            Console.WriteLine("Accept " + socket.RemoteEndPoint.ToString());
            //创建描述客户端的对象
            ClientState state = new ClientState();
            state.socket = socket;

            receiveClientUdp.BeginReceive(ReceiveUdpClientCallback, state);


            uint guid = MyGuid.GetGuid();
            state.guid = guid;
            id2cs.Add(guid, state);

            state.lastPingTime = GetTimeStamp();
            clientStates.Add(socket, state);
        }
        catch (SocketException e)
        {
            Console.WriteLine("Accept 失败" + e.Message);
        }
    }
    /// <summary>
    /// 接收客户端发过来的消息
    /// </summary>
    /// <param name="socket">客户端的socket</param>
    private static void Receive(Socket socket)
    {
        ClientState state = clientStates[socket];
        ByteArray readBuffer = state.readBuffer;

        if (readBuffer.Remain <= 0)
        {
            readBuffer.MoveBytes();
        }
        if (readBuffer.Remain <= 0)
        {
            Console.WriteLine("Receive 失败,数组不够大");
            return;
        }
        int count = 0;
        try
        {
            count = socket.Receive(readBuffer.bytes, readBuffer.writeIndex, readBuffer.Remain, 0);
        }
        catch (SocketException e)
        {
            Console.WriteLine("Receive 失败," + e.Message);
            return;
        }
        //客户端主动关闭
        if (count <= 0)
        {
            Console.WriteLine("Socket Close :" + socket.RemoteEndPoint.ToString());
            return;
        }
        readBuffer.writeIndex += count;
        //处理消息
        OnReceiveData(state);
        readBuffer.MoveBytes();
    }
    /// <summary>
    /// 处理消息
    /// </summary>
    /// <param name="state">客户端对象</param>
    private static void OnReceiveData(ClientState state)
    {
        ByteArray readBuffer = state.readBuffer;
        byte[] bytes = readBuffer.bytes;
        int readIndex = readBuffer.readIndex;

        if (readBuffer.Length <= 2)
            return;
        //解析总长度
        short length = (short)(bytes[readIndex + 1] * 256 + bytes[readIndex]);
        //收到的消息没有解析出来的多
        if (readBuffer.Length < length + 2)
            return;

        ServerType serverType = (ServerType)bytes[readIndex + 2];
        readBuffer.readIndex += 3;


        try
        {
            //减去一个字节的服务器号，留四位作为id 得到发送出去消息的长度
            int sendLength = length - 1 + 4;
            byte[] sendBytes = new byte[sendLength + 2];
            sendBytes[0] = (byte)(sendLength % 256);
            sendBytes[1] = (byte)(sendLength / 256);

            sendBytes[2] = (byte)(state.guid >> 24);
            sendBytes[3] = (byte)((state.guid >> 16) & 0xff);
            sendBytes[4] = (byte)((state.guid >> 8) & 0xff);
            sendBytes[5] = (byte)(state.guid & 0xff);

            Array.Copy(bytes, readBuffer.readIndex, sendBytes, 6, sendLength - 4);
            type2ss[serverType].socket.Send(sendBytes, 0, sendLength + 2, 0);
        }
        catch (SocketException e)
        {
            Console.WriteLine(e.Message);
        }

        readBuffer.readIndex += length - 1;
        readBuffer.MoveBytes();

        //继续处理
        if (readBuffer.Length > 2)
        {
            OnReceiveData(state);
        }

        //int nameCount = 0;
        //string protoName = MsgBase.DecodeName(readBuffer.bytes, readBuffer.readIndex, out nameCount);
        //if (protoName == "")
        //{
        //    Console.WriteLine("OnReceiveData 失败,协议名为空");
        //    return;
        //}
        //readBuffer.readIndex += nameCount;


        //int bodyLength = length - nameCount;
        //MsgBase msgBase = MsgBase.Decode(protoName, readBuffer.bytes, readBuffer.readIndex, bodyLength);
        //readBuffer.readIndex += bodyLength;
        //readBuffer.MoveBytes();

        ////通过反射调用客户端发过来的协议对应的方法
        //MethodInfo mi = typeof(MsgHandler).GetMethod(protoName);
        //Console.WriteLine("Receive:" + protoName);
        //if (mi != null)
        //{
        //    //要执行方法的参数
        //    object[] o = { state, msgBase };
        //    mi.Invoke(null, o);
        //}
        //else
        //{
        //    Console.WriteLine("OnReceiveData 反射失败");
        //}

        //if (readBuffer.Length > 2)
        //{
        //    OnReceiveData(state);
        //}
    }
    /// <summary>
    /// 发送消息
    /// </summary>
    /// <param name="state">客户端对象</param>
    /// <param name="msgBase">消息</param>
    public static void Send(ClientState state, MsgBase msgBase)
    {
        if (state == null || !state.socket.Connected)
            return;

        //编码
        byte[] nameBytes = MsgBase.EncodeName(msgBase);
        byte[] bodyBytes = MsgBase.Encode(msgBase);
        int len = nameBytes.Length + bodyBytes.Length;
        byte[] sendBytes = new byte[len + 2];
        sendBytes[0] = (byte)(len % 256);
        sendBytes[1] = (byte)(len / 256);
        Array.Copy(nameBytes, 0, sendBytes, 2, nameBytes.Length);
        Array.Copy(bodyBytes, 0, sendBytes, 2 + nameBytes.Length, bodyBytes.Length);

        try
        {
            state.socket.Send(sendBytes, 0, sendBytes.Length, 0);
        }
        catch (SocketException e)
        {
            Console.WriteLine("Send 失败" + e.Message);
        }
    }
    /// <summary>
    /// 关闭对应的客户端
    /// </summary>
    /// <param name="state">客户端</param>
    private static void Close(ClientState state)
    {
        state.socket.Close();
        clientStates.Remove(state.socket);
    }
    /// <summary>
    /// 获取时间戳
    /// </summary>
    /// <returns>时间戳</returns>
    public static long GetTimeStamp()
    {
        TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
        return Convert.ToInt64(ts.TotalSeconds);
    }
    private static void CheckPing()
    {
        foreach (ClientState state in clientStates.Values)
        {
            if (GetTimeStamp() - state.lastPingTime > pingInterval * 4)
            {
                Console.WriteLine("心跳机制，断开连接:", state.socket.RemoteEndPoint);
                //关闭客户端
                Close(state);
                return;
            }
        }
    }

    #region Udp
    private static void ReceiveUdpClientCallback(IAsyncResult ar)
    {
        ClientState state = (ClientState)ar.AsyncState;
        IPEndPoint iPEndPoint=new IPEndPoint(IPAddress.Any, 0);
        byte[] receiveBuf=receiveClientUdp.EndReceive(ar, ref iPEndPoint);
        ServerType serverType = (ServerType)receiveBuf[0];

        //要给发送消息的服务器的IP地址
        IPEndPoint serverIpendPoint = (IPEndPoint)type2ss[serverType].socket.RemoteEndPoint;
        byte[] sendBytes = new byte[receiveBuf.Length + 3];
        //打包guid
        sendBytes[0] = (byte)(state.guid >> 24);
        sendBytes[1]=(byte)((state.guid >> 16)&0xff);
        sendBytes[2]=(byte)((state.guid >> 8)&0xff);
        sendBytes[3]=(byte)(state.guid & 0xff);

        Array.Copy(receiveBuf, 1, sendBytes, 4, receiveBuf.Length - 1);
        
        receiveServerUdp.Send(sendBytes,sendBytes.Length, serverIpendPoint);

        receiveClientUdp.BeginReceive(ReceiveUdpClientCallback, state);
    }
    private static void ReceiveUdpServerCallback(IAsyncResult ar)
    {
        ServerState state = (ServerState)ar.AsyncState;

        IPEndPoint iPEndPoint=new IPEndPoint(IPAddress.Any,0);
        byte[] receiveBuf = receiveServerUdp.EndReceive(ar,ref iPEndPoint);

        uint guid = (uint)(receiveBuf[0] << 24 |
                    receiveBuf[1] << 16 |
                    receiveBuf[2] << 8 |
                    receiveBuf[3]);


        IPEndPoint clientIpEndPoint = (IPEndPoint)id2cs[guid].socket.RemoteEndPoint;

        Array.Copy(receiveBuf,4,receiveBuf,0,receiveBuf.Length-4);

        receiveClientUdp.Send(receiveBuf,receiveBuf.Length-4, clientIpEndPoint);

        receiveServerUdp.BeginReceive(ReceiveUdpServerCallback, state);
    }
    #endregion
}
