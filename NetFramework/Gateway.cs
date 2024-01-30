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
        Gateway,//���ط�����
        Fighter,//ս��������
    }
    /// <summary>
    /// �������ӿͻ��˵ķ����socket
    /// </summary>
    public static Socket listenfd;
    /// <summary>
    /// ����������������˵�Socket
    /// </summary>
    public static Socket gateway;
    /// <summary>
    /// �ͻ����ֵ�
    /// </summary>
    public static Dictionary<Socket, ClientState> clientStates = new Dictionary<Socket, ClientState>();
    /// <summary>
    /// ����������ֵ�
    /// </summary>
    public static Dictionary<Socket, ServerState> serverStates = new Dictionary<Socket, ServerState>();

    /// <summary>
    /// ���������ͺͷ�������ӳ��
    /// </summary>
    public static Dictionary<ServerType, ServerState> type2ss = new Dictionary<ServerType, ServerState>();
    /// <summary>
    /// ͨ��id�ҵ���Ӧ�ͻ��˵��ֵ�
    /// </summary>
    public static Dictionary<uint, ClientState> id2cs = new Dictionary<uint, ClientState>();
    /// <summary>
    /// ���ڼ����б�
    /// </summary>
    public static List<Socket> sockets = new List<Socket>();
    private static float pingInterval = 2;

    /// <summary>
    /// ���տͻ��˵�udp
    /// </summary>
    private static UdpClient receiveClientUdp;
    /// <summary>
    /// ���շ���˵�udp
    /// </summary>
    private static UdpClient receiveServerUdp;
    /// <summary>
    /// ���ӷ�����
    /// </summary>
    /// <param name="ip">ip��ַ</param>
    /// <param name="port">�˿ں�</param>
    public static void Connect(string ip, int port)
    {
        listenfd = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        IPAddress iPAddress = IPAddress.Parse(ip);
        IPEndPoint iPEndPoint = new IPEndPoint(iPAddress, port);
        listenfd.Bind(iPEndPoint);
        listenfd.Listen(0);


        receiveClientUdp = new UdpClient((IPEndPoint)listenfd.LocalEndPoint);

        Console.WriteLine("�����������ɹ�");
        while (true)
        {
            sockets.Clear();
            //�ŷ���˵�socket
            sockets.Add(listenfd);
            //�ſͻ��˵�Socket
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
                    //�пͻ���Ҫ����
                    Accept(s);
                }
                else
                {
                    //�ͻ��˷���Ϣ������
                    Receive(s);
                }
            }
            //CheckPing();
        }
    }

    /// <summary>
    /// ��������������
    /// </summary>
    /// <param name="ip">ip��ַ</param>
    /// <param name="port">�˿ں�</param>
    public static ServerState ConnectServer(string ip, int port)
    {
        ServerState serverState = new ServerState();
        gateway = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        IPAddress ipAddress = IPAddress.Parse(ip);
        IPEndPoint iPEndPoint = new IPEndPoint(ipAddress, port);
        gateway.Bind(iPEndPoint);
        gateway.Listen(0);
        Console.WriteLine("���ط������ȴ���������������");
        gateway.BeginAccept(AcceptServerCallback, serverState);
        return serverState;
    }
    /// <summary>
    /// ����������������ӵĻص�
    /// </summary>
    /// <param name="ar"></param>
    private static void AcceptServerCallback(IAsyncResult ar)
    {
        //��װ���ӹ����ķ���˶���
        ServerState serverState = (ServerState)ar.AsyncState;
        Socket socket = gateway.EndAccept(ar);
        Console.WriteLine("���ӳɹ�");
        serverState.socket = socket;

        serverStates.Add(socket, serverState);


        //udp
        receiveServerUdp = new UdpClient((IPEndPoint)gateway.LocalEndPoint);
        receiveServerUdp.BeginReceive(ReceiveUdpServerCallback, serverState);

        //������Ϣ
        ByteArray byteArray = serverState.readBuffer;
        socket.BeginReceive(byteArray.bytes, byteArray.writeIndex, byteArray.Remain, 0, ReceiveServerCallback, serverState);
    }
    /// <summary>
    /// ������������˷���������Ϣ�ص�
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
            Console.WriteLine("Receive fail :���鳤�Ȳ���");
            //�رշ����
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
            //�رշ����
            //Close();
            return;
        }
        if (count <= 0)
        {
            Console.WriteLine("Socket Close:" + serverState.socket.RemoteEndPoint.ToString());
            //�رշ����
            //Close();
            return;
        }

        //������չ�������Ϣ
        byteArray.writeIndex += count;
        OnReceiveData(serverState);
        byteArray.MoveBytes();

        server.BeginReceive(byteArray.bytes, byteArray.writeIndex, byteArray.Remain, 0, ReceiveServerCallback, serverState);
    }
    /// <summary>
    /// �������˷���������Ϣ
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
        //��������
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
            //���͸��ͻ��˵�����
            byte[] sendBytes = new byte[msgLength + 2];
            //�������
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

        //��������
        if (byteArray.Length > 2)
        {
            OnReceiveData(serverState);
        }
    }
    /// <summary>
    /// ���տͻ��˵�����
    /// </summary>
    /// <param name="listenfd">����˵�socket</param>
    private static void Accept(Socket listenfd)
    {
        try
        {
            Socket socket = listenfd.Accept();
            Console.WriteLine("Accept " + socket.RemoteEndPoint.ToString());
            //���������ͻ��˵Ķ���
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
            Console.WriteLine("Accept ʧ��" + e.Message);
        }
    }
    /// <summary>
    /// ���տͻ��˷���������Ϣ
    /// </summary>
    /// <param name="socket">�ͻ��˵�socket</param>
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
            Console.WriteLine("Receive ʧ��,���鲻����");
            return;
        }
        int count = 0;
        try
        {
            count = socket.Receive(readBuffer.bytes, readBuffer.writeIndex, readBuffer.Remain, 0);
        }
        catch (SocketException e)
        {
            Console.WriteLine("Receive ʧ��," + e.Message);
            return;
        }
        //�ͻ��������ر�
        if (count <= 0)
        {
            Console.WriteLine("Socket Close :" + socket.RemoteEndPoint.ToString());
            return;
        }
        readBuffer.writeIndex += count;
        //������Ϣ
        OnReceiveData(state);
        readBuffer.MoveBytes();
    }
    /// <summary>
    /// ������Ϣ
    /// </summary>
    /// <param name="state">�ͻ��˶���</param>
    private static void OnReceiveData(ClientState state)
    {
        ByteArray readBuffer = state.readBuffer;
        byte[] bytes = readBuffer.bytes;
        int readIndex = readBuffer.readIndex;

        if (readBuffer.Length <= 2)
            return;
        //�����ܳ���
        short length = (short)(bytes[readIndex + 1] * 256 + bytes[readIndex]);
        //�յ�����Ϣû�н��������Ķ�
        if (readBuffer.Length < length + 2)
            return;

        ServerType serverType = (ServerType)bytes[readIndex + 2];
        readBuffer.readIndex += 3;


        try
        {
            //��ȥһ���ֽڵķ������ţ�����λ��Ϊid �õ����ͳ�ȥ��Ϣ�ĳ���
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

        //��������
        if (readBuffer.Length > 2)
        {
            OnReceiveData(state);
        }

        //int nameCount = 0;
        //string protoName = MsgBase.DecodeName(readBuffer.bytes, readBuffer.readIndex, out nameCount);
        //if (protoName == "")
        //{
        //    Console.WriteLine("OnReceiveData ʧ��,Э����Ϊ��");
        //    return;
        //}
        //readBuffer.readIndex += nameCount;


        //int bodyLength = length - nameCount;
        //MsgBase msgBase = MsgBase.Decode(protoName, readBuffer.bytes, readBuffer.readIndex, bodyLength);
        //readBuffer.readIndex += bodyLength;
        //readBuffer.MoveBytes();

        ////ͨ��������ÿͻ��˷�������Э���Ӧ�ķ���
        //MethodInfo mi = typeof(MsgHandler).GetMethod(protoName);
        //Console.WriteLine("Receive:" + protoName);
        //if (mi != null)
        //{
        //    //Ҫִ�з����Ĳ���
        //    object[] o = { state, msgBase };
        //    mi.Invoke(null, o);
        //}
        //else
        //{
        //    Console.WriteLine("OnReceiveData ����ʧ��");
        //}

        //if (readBuffer.Length > 2)
        //{
        //    OnReceiveData(state);
        //}
    }
    /// <summary>
    /// ������Ϣ
    /// </summary>
    /// <param name="state">�ͻ��˶���</param>
    /// <param name="msgBase">��Ϣ</param>
    public static void Send(ClientState state, MsgBase msgBase)
    {
        if (state == null || !state.socket.Connected)
            return;

        //����
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
            Console.WriteLine("Send ʧ��" + e.Message);
        }
    }
    /// <summary>
    /// �رն�Ӧ�Ŀͻ���
    /// </summary>
    /// <param name="state">�ͻ���</param>
    private static void Close(ClientState state)
    {
        state.socket.Close();
        clientStates.Remove(state.socket);
    }
    /// <summary>
    /// ��ȡʱ���
    /// </summary>
    /// <returns>ʱ���</returns>
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
                Console.WriteLine("�������ƣ��Ͽ�����:", state.socket.RemoteEndPoint);
                //�رտͻ���
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

        //Ҫ��������Ϣ�ķ�������IP��ַ
        IPEndPoint serverIpendPoint = (IPEndPoint)type2ss[serverType].socket.RemoteEndPoint;
        byte[] sendBytes = new byte[receiveBuf.Length + 3];
        //���guid
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
