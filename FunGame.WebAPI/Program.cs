using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Milimoe.FunGame.Core.Api.Utility;
using Milimoe.FunGame.Core.Library.Common.Addon;
using Milimoe.FunGame.Core.Library.Common.JsonConverter;
using Milimoe.FunGame.Core.Library.Common.Network;
using Milimoe.FunGame.Core.Library.Constant;
using Milimoe.FunGame.Server.Controller;
using Milimoe.FunGame.Server.Model;
using Milimoe.FunGame.Server.Others;
using Milimoe.FunGame.Server.Utility;
using Milimoe.FunGame.WebAPI.Architecture;
using Milimoe.FunGame.WebAPI.Services;

WebAPIListener listener = new();

try
{
    Console.Title = Config.ServerName;
    Console.WriteLine(FunGameInfo.GetInfo(Config.FunGameType));

    ServerHelper.WriteLine("���ڶ�ȡ�����ļ�����ʼ������ . . .");
    // ��ʼ������˵�
    ServerHelper.InitOrderList();

    // ����ȫ��SQLHelper
    FunGameSystem.InitSQLHelper();

    // ����ȫ��MailSender
    FunGameSystem.InitMailSender();

    // ��ȡ��Ϸģ��
    if (!FunGameSystem.GetGameModuleList())
    {
        ServerHelper.WriteLine("�������ƺ�δ��װ�κ���Ϸģ�飬�����Ƿ���ȷ��װ���ǡ�");
    }

    // ��ȡServer���
    FunGameSystem.GetServerPlugins();

    // ��ȡWeb API���
    FunGameSystem.GetWebAPIPlugins();

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

    // ��������
    RESTfulAPIListener apiListener = new();
    RESTfulAPIListener.Instance = apiListener;

    ServerHelper.WriteLine("������ help ����ȡ���������� Ctrl+C �رշ�������");

    ServerHelper.PrintFunGameTitle();

    if (Config.ServerNotice != "")
        Console.WriteLine("\r \n********** ���������� **********\n\n" + Config.ServerNotice + "\n");
    else
        Console.WriteLine("�޷���ȡ����������");

    ServerHelper.WriteLine("�������� Web API ���� . . .");
    Console.WriteLine("\r ");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // Add services to the container.
    // ��ȡ��չ������
    if (Config.WebAPIPluginLoader != null)
    {
        foreach (WebAPIPlugin plugin in Config.WebAPIPluginLoader.Plugins.Values)
        {
            Assembly? pluginAssembly = Assembly.GetAssembly(plugin.GetType());

            if (pluginAssembly != null)
            {
                // ע�����п�����
                builder.Services.AddControllers().PartManager.ApplicationParts.Add(new AssemblyPart(pluginAssembly));
            }
        }
    }
    // ��� JSON ת����
    builder.Services.AddControllers().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        foreach (JsonConverter converter in JsonTool.JsonSerializerOptions.Converters)
        {
            options.JsonSerializerOptions.Converters.Add(converter);
        }
    });
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "���� Auth ���ص� BearerToken",
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });
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
    // ��� JWT ��֤
    builder.Services.AddScoped<JWTService>();
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? "undefined"))
        };
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

    app.UseDefaultFiles();

    app.UseStaticFiles();

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

    // ��׽�رճ����¼�
    IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(CloseServer);

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

void CloseServer()
{
    FunGameSystem.CloseServer();
}
