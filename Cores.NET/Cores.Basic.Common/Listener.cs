using System;
using System.Threading;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace IPA.Cores.Basic
{
    enum ListenerStatus
    {
        Trying = 0,
        Listening = 1,
    }

    delegate void AcceptProc(Listener listener, Sock sock, object param);

    class Listener
    {
        public const int ListenRetryTimeDefault = 2 * 1000;

        int listenRetryTime;
        public int ListenRetryTime
        {
            get { return listenRetryTime; }
            set { listenRetryTime = value; }
        }
        object lockObj;
        int port;
        public int Port
        {
            get { return port; }
        }
        ThreadObj thread;
        Sock sock;
        public Sock Sock
        {
            get { return sock; }
        }
        Event eventObj;
        bool halt;
        ListenerStatus status;
        public ListenerStatus Status
        {
            get { return status; }
        }
        AcceptProc acceptProc;
        public AcceptProc AcceptProc
        {
            get { return acceptProc; }
        }
        object acceptParam;
        public object AcceptParam
        {
            get { return acceptParam; }
        }
        bool localOnly;
        public bool LocalOnly
        {
            get { return localOnly; }
        }
        bool getHostName;
        public bool GetHostName
        {
            get { return getHostName; }
            set { getHostName = value; }
        }

        public Listener(int port, AcceptProc acceptProc, object acceptParam)
        {
            init(port, acceptProc, acceptParam, false, false);
        }
        public Listener(int port, AcceptProc acceptProc, object acceptParam, bool localOnly)
        {
            init(port, acceptProc, acceptParam, localOnly, false);
        }
        public Listener(int port, AcceptProc acceptProc, object acceptParam, bool localOnly, bool getHostName)
        {
            init(port, acceptProc, acceptParam, localOnly, getHostName);
        }

        // 初期化
        void init(int port, AcceptProc acceptProc, object acceptParam, bool localOnly, bool getHostName)
        {
            this.lockObj = new object();
            this.port = port;
            this.acceptProc = acceptProc;
            this.acceptParam = acceptParam;
            this.status = ListenerStatus.Trying;
            this.eventObj = new Event();
            this.halt = false;
            this.localOnly = localOnly;
            this.getHostName = getHostName;
            this.listenRetryTime = ListenRetryTimeDefault;

            // スレッドの作成
            ThreadObj thread = new ThreadObj(new ThreadProc(ListenerThread));

            thread.WaitForInit();
        }

        // 停止
        public void Stop()
        {
            Sock s;

            lock (this.lockObj)
            {
                if (this.halt)
                {
                    return;
                }

                this.halt = true;

                s = this.sock;
            }

            if (s != null)
            {
                s.Disconnect();
            }

            this.eventObj.Set();

            this.thread.WaitForEnd();
        }

        // TCP 受付完了
        void tcpAccepted(Sock s)
        {
            ThreadObj t = new ThreadObj(new ThreadProc(tcpAcceptedThread), s);
            t.WaitForInit();
        }

        void tcpAcceptedThread(object param)
        {
            Sock s = (Sock)param;

            ThreadObj.NoticeInited();

            this.acceptProc(this, s, this.acceptParam);
        }

        // スレッド
        public void ListenerThread(object param)
        {
            Sock new_sock, s;
            int num_failed;

            this.thread = ThreadObj.GetCurrentThreadObj();

            this.status = ListenerStatus.Trying;

            ThreadObj.NoticeInited();

            while (true)
            {
                bool firstFailed = true;
                this.status = ListenerStatus.Trying;

                // Listen を試みる
                while (true)
                {
                    if (this.halt)
                    {
                        return;
                    }

                    try
                    {
                        s = Sock.Listen(this.port, this.localOnly);

                        this.sock = s;

                        break;
                    }
                    catch
                    {
                        if (firstFailed)
                        {
                            firstFailed = false;
                        }

                        this.eventObj.Wait(this.listenRetryTime);

                        if (this.halt)
                        {
                            return;
                        }
                    }
                }

                this.status = ListenerStatus.Listening;

                if (this.halt)
                {
                    this.sock.Disconnect();
                    break;
                }

                num_failed = 0;

                // Accept ループ
                while (true)
                {
                    // Accept する
                    new_sock = this.sock.Accept(this.getHostName);
                    if (new_sock != null)
                    {
                        // 成功
                        tcpAccepted(new_sock);
                    }
                    else
                    {
                        // 失敗
                        if (this.halt == false)
                        {
                            if ((++num_failed) <= 5)
                            {
                                continue;
                            }
                        }

                        this.sock.Disconnect();
                        break;
                    }
                }

                if (this.halt)
                {
                    return;
                }
            }
        }
    }
}
