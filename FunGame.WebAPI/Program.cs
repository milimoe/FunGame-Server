using System.Net.WebSockets;
using Microsoft.AspNetCore.Diagnostics;
using Milimoe.FunGame.Core.Api.Utility;
using Milimoe.FunGame.Core.Library.Common.Network;
using Milimoe.FunGame.Core.Library.Constant;
using Milimoe.FunGame.Server.Controller;
using Milimoe.FunGame.Server.Model;
using Milimoe.FunGame.Server.Others;
using Milimoe.FunGame.Server.Utility;
using Milimoe.FunGame.WebAPI.Architecture;

WebAPIListener listener = new();

try
{
    Console.Title = Config.ServerName;
    Console.WriteLine(FunGameInfo.GetInfo(Config.FunGameType));

    ServerHelper.WriteLine("���ڶ�ȡ�����ļ�����ʼ������ . . .");
    // ��ʼ������˵�
    ServerHelper.InitOrderList();

    // ��ȡ��Ϸģ��
    if (!Config.GetGameModuleList())
    {
        ServerHelper.WriteLine("�������ƺ�δ��װ�κ���Ϸģ�飬�����Ƿ���ȷ��װ���ǡ�");
    }

    // ����Ƿ���������ļ�
    if (!INIHelper.ExistINIFile())
    {
        ServerHelper.WriteLine("δ��⵽�����ļ������Զ����������ļ� . . .");
        INIHelper.Init(Config.FunGameType);
        ServerHelper.WriteLine("�����ļ�FunGame.ini�����ɹ������޸ĸ������ļ���Ȼ��������������");
        Console.ReadKey();
        return;
    }
    else
    {
        ServerHelper.GetServerSettings();
    }

    ServerHelper.WriteLine("������ help ����ȡ���������� quit �رշ�������");

    // ����ȫ��SQLHelper
    Config.InitSQLHelper();

    ServerHelper.WriteLine("�������� Web API ���� . . .");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // Add services to the container.
    builder.Services.AddControllers();
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    // ��� CORS ����
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowSpecificOrigin", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    WebApplication app = builder.Build();

    // ���� CORS
    app.UseCors("AllowSpecificOrigin");

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            IExceptionHandlerFeature? contextFeature = context.Features.Get<IExceptionHandlerFeature>();
            if (contextFeature != null)
            {
                await context.Response.WriteAsync(new
                {
                    context.Response.StatusCode,
                    Message = "Internal Server Error.",
                    Detailed = contextFeature.Error.Message
                }.ToString() ?? "");
            }
        });
    });

    // ���� WebSockets �м��
    WebSocketOptions webSocketOptions = new()
    {
        KeepAliveInterval = TimeSpan.FromMinutes(2) // ���� WebSocket �ı�����
    };
    app.UseWebSockets(webSocketOptions);

    // ·�ɵ� WebSocket ������
    app.Map("/ws", WebSocketConnectionHandler);

    // ��ʼ��������
    listener.BannedList.AddRange(Config.ServerBannedList);

    if (Config.ServerNotice != "")
        Console.WriteLine("\n\n********** ���������� **********\n\n" + Config.ServerNotice + "\n");
    else
        Console.WriteLine("�޷���ȡ����������");

    Task order = Task.Factory.StartNew(GetConsoleOrder);

    app.Run();
}
catch (Exception e)
{
    ServerHelper.Error(e);
}

async Task GetConsoleOrder()
{
    while (true)
    {
        string order = Console.ReadLine() ?? "";
        ServerHelper.Type();
        if (order != "")
        {
            order = order.ToLower();
            switch (order)
            {
                default:
                    await ConsoleModel.Order(listener, order);
                    break;
            }
        }
    }
}

async Task WebSocketConnectionHandler(HttpContext context)
{
    string clientip = "";
    try
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            WebSocket instance = await context.WebSockets.AcceptWebSocketAsync();
            clientip = context.Connection.RemoteIpAddress?.ToString() + ":" + context.Connection.RemotePort;

            Guid token = Guid.NewGuid();
            ServerWebSocket socket = new(listener, instance, clientip, clientip, token);
            Config.ConnectingPlayerCount++;
            bool isConnected = false;
            bool isDebugMode = false;

            // ��ʼ����ͻ�����������
            IEnumerable<SocketObject> objs = [];
            while (!objs.Any(o => o.SocketType == SocketMessageType.Connect))
            {
                objs = objs.Union(await socket.ReceiveAsync());
            }
            (isConnected, isDebugMode) = await ConnectController.Connect(listener, socket, token, clientip, objs.Where(o => o.SocketType == SocketMessageType.Connect));
            if (isConnected)
            {
                ServerModel<ServerWebSocket> ClientModel = new(listener, socket, isDebugMode);
                ClientModel.SetClientName(clientip);
                await ClientModel.Start();
            }
            else
            {
                ServerHelper.WriteLine(ServerHelper.MakeClientName(clientip) + " ����ʧ�ܡ�", InvokeMessageType.Core);
                await socket.CloseAsync();
            }
            Config.ConnectingPlayerCount--;
        }
        else
        {
            context.Response.StatusCode = 400;
        }
    }
    catch (Exception e)
    {
        if (--Config.ConnectingPlayerCount < 0) Config.ConnectingPlayerCount = 0;
        ServerHelper.WriteLine(ServerHelper.MakeClientName(clientip) + " �ж����ӣ�", InvokeMessageType.Core);
        ServerHelper.Error(e);
    }
}
