﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rests;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using Terraria;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;


namespace CaiBot;

[ApiVersion(2, 1)]
public class Plugin : TerrariaPlugin
{
    public override string Author => "Cai,羽学,西江";
    public override string Description => "CaiBot机器人的适配插件";
    public override string Name => "CaiBotPlugin";
    public static readonly Version VersionNum = new(2024, 10, 20 , 1); //日期+版本号(0,1,2...)
    public override Version Version => VersionNum;
    
    public Plugin(Main game) : base(game)
    {
    }

    public static int InitCode = -1;

    public static ClientWebSocket WebSocket = new();
    public static Task WebSocketTask = Task.CompletedTask;
    public static readonly CancellationTokenSource TokenSource = new ();
    public Task WsTask;
    public Task HeartBeat;

    #region 加载前置

    private Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var resourceName =
            $"{Assembly.GetExecutingAssembly().GetName().Name}.{new AssemblyName(args.Name).Name}.dll";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            var assemblyData = new byte[stream.Length];
            stream.Read(assemblyData, 0, assemblyData.Length);
            return Assembly.Load(assemblyData);
        }
        return null;
    }

    #endregion

    public override void Initialize()
    {
        // Commands.ChatCommands.Add(new Command( TestCommand,"test"));
        Config.Read();
        AppDomain.CurrentDomain.AssemblyResolve += this.CurrentDomain_AssemblyResolve;
        On.OTAPI.Hooks.MessageBuffer.InvokeGetData += this.MessageBuffer_InvokeGetData;
        On.OTAPI.Hooks.MessageBuffer.InvokeGetData += Login.MessageBuffer_InvokeGetData;
        ServerApi.Hooks.NetGetData.Register(this, Login.OnGetData, int.MaxValue);
        ServerApi.Hooks.GamePostInitialize.Register(this, this.GenCode);
        this.WsTask = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    WebSocket = new ClientWebSocket();
                    while (Config.config.Token == "")
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        HttpClient client = new();
                        HttpResponseMessage? response;
                        client.Timeout = TimeSpan.FromSeconds(5.0);
                        response = client.GetAsync($"http://api.terraria.ink:22334/bot/get_token?" +
                                                   $"code={InitCode}")
                            .Result;
                        //TShock.Log.ConsoleInfo($"[CaiAPI]尝试被动绑定,状态码:{response.StatusCode}");
                        if (response.StatusCode == HttpStatusCode.OK && Config.config.Token == "")
                        {
                            var responseBody = await response.Content.ReadAsStringAsync();
                            var json = JObject.Parse(responseBody);
                            var token = json["token"]!.ToString();
                            Config.config.Token = token;
                            Config.config.Write();
                            TShock.Log.ConsoleInfo($"[CaiAPI]被动绑定成功!");
                        }

                    }

                    if (Terraria.Program.LaunchParameters.ContainsKey("-cailocalbot"))
                    {
                        await WebSocket.ConnectAsync(new Uri("ws://127.0.0.1:22334/bot/" + Config.config.Token),
                            CancellationToken.None);
                    }
                    else
                    {
                        await WebSocket.ConnectAsync(new Uri("ws://api.terraria.ink:22334/bot/" + Config.config.Token),
                            CancellationToken.None);
                    }

                    while (true)
                    {
                        var buffer = new byte[1024];
                        var result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer),
                            CancellationToken.None);
                        var receivedData = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        if (Terraria.Program.LaunchParameters.ContainsKey("-caidebug"))
                        {
                            TShock.Log.ConsoleInfo($"[CaiAPI]收到BOT数据包: {receivedData}");
                        }

                        MessageHandle.HandleMessageAsync(receivedData);
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleInfo($"[CaiAPI]CaiBot断开连接...");
                    if (Terraria.Program.LaunchParameters.ContainsKey("-caidebug"))
                    {
                        TShock.Log.ConsoleError(ex.ToString());
                    }
                    else
                    {
                        TShock.Log.ConsoleError("链接失败原因: " + ex.Message);
                    }
                }

                await Task.Delay(5000);
            }
        },TokenSource.Token);
        this.HeartBeat = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(60000);
                try
                {
                    if (WebSocket.State == WebSocketState.Open)
                    {
                        Dictionary<string, string> heartBeat = new()
                        {
                            { "type", "HeartBeat" }
                        };
                        await MessageHandle.SendDateAsync(JsonConvert.SerializeObject(heartBeat));
                    }
                }
                catch
                {
                    TShock.Log.ConsoleInfo("[CaiBot]心跳包发送失败!");
                }
            }
        },TokenSource.Token);
        EconomicSupport.Init();
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= this.CurrentDomain_AssemblyResolve;
            On.OTAPI.Hooks.MessageBuffer.InvokeGetData -= this.MessageBuffer_InvokeGetData;
            On.OTAPI.Hooks.MessageBuffer.InvokeGetData -= Login.MessageBuffer_InvokeGetData;
            ServerApi.Hooks.NetGetData.Deregister(this, Login.OnGetData);
            ServerApi.Hooks.GamePostInitialize.Deregister(this, this.GenCode);
            if (!WebSocketTask.IsCompleted)
            {
                TokenSource.Cancel();
                TokenSource.Dispose();
            }
        }
        base.Dispose(disposing);
    }
    private void GenCode(EventArgs args)
    {
        if (!string.IsNullOrEmpty(Config.config.Token))
        {
            return;
        }

        InitCode = new Random().Next(10000000, 99999999);
        TShock.Log.ConsoleError($"[CaiBot]您的服务器绑定码为: {InitCode}");
    }

    private bool MessageBuffer_InvokeGetData(On.OTAPI.Hooks.MessageBuffer.orig_InvokeGetData orig,
        MessageBuffer instance, ref byte packetId, ref int readOffset, ref int start, ref int length,
        ref int messageType, int maxPackets)
    {
        if (messageType == 217)
        {
            if (!string.IsNullOrEmpty(Config.config.Token))
            {
                NetMessage.SendData(2, instance.whoAmI, -1, NetworkText.FromFormattable("exist"));
                TShock.Log.ConsoleInfo($"[CaiAPI]试图绑定已绑定服务器!");
                return false;
            }

            instance.ResetReader();
            instance.reader.BaseStream.Position = start + 1;
            var data = instance.reader.ReadString();
            var token = Guid.NewGuid().ToString();
            if (data == InitCode.ToString())
            {
                NetMessage.SendData(2, instance.whoAmI, -1, NetworkText.FromFormattable(token));
                Config.config.Token = token;
                Config.config.Write();
                TShock.Log.ConsoleInfo($"[CaiAPI]主动绑定成功!");
                return false;
            }
            else
            {
                NetMessage.SendData(2, instance.whoAmI, -1, NetworkText.FromFormattable("code"));
            }
        }

        
        return orig(instance, ref packetId, ref readOffset, ref start, ref length, ref messageType, maxPackets);;
    }

    
}