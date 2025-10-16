
using LuoliCommon;
using LuoliCommon.Logger;
using LuoliHelper.Utils;
using LuoliUtils;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using RabbitMQ.Client;
using SqlSugar;
using System.Net;
using System.Reflection;

namespace ExternalOrderService
{
    public class Program
    {
        public static Config Config { get; set; }
        private static SqlSugarConnection MasterDB { get; set; }
        private static SqlSugarConnection SlaverDB { get; set; }
        private static RabbitMQConnection RabbitMQConnection { get; set; }
        private static RedisConnection RedisConnection { get; set; }

        private static bool init()
        {
            bool result = false;
            string configFolder = "/app/ExternalOrder/configs";

#if DEBUG
            configFolder = "debugConfigs";
#endif

            ActionsOperator.TryCatchAction(() =>
            {
                Config = new Config($"{configFolder}/sys.json");
                MasterDB = new SqlSugarConnection($"{configFolder}/master_db.json");
                SlaverDB = new SqlSugarConnection($"{configFolder}/slaver_db.json");
                RabbitMQConnection = new RabbitMQConnection($"{configFolder}/rabbitmq.json");
                RedisConnection = new RedisConnection($"{configFolder}/redis.json");

                var rds = new CSRedis.CSRedisClient($"{RedisConnection.Host}:{RedisConnection.Port},password={RedisConnection.Password},defaultDatabase={RedisConnection.DatabaseId}");
                RedisHelper.Initialization(rds);

                //SqlClient.DbFirst.IsCreateAttribute().StringNullable().CreateClassFile(@"E:\Code\repos\LuoliHelper\DBModels", "LuoliHelper.DBModels");

                result = true;
            });

            return result;
        }



        public static void Main(string[] args)
        {

            #region luoli code

            Environment.CurrentDirectory = AppContext.BaseDirectory;

            if (!init())
            {
                throw new Exception("initial failed; cannot start");
            }

            #endregion


            var builder = WebApplication.CreateBuilder(args);

            // 配置 Kestrel 支持 HTTP/2
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                int port = int.Parse(Config.KVPairs["BindPort"]);

                serverOptions.ListenAnyIP(port, options => {
                    options.Protocols = HttpProtocols.Http1AndHttp2;
                });
            });


            // Add services to the container.

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();


            #region 注册ILogger

            builder.Services.AddHttpClient("LokiHttpClient")
                .ConfigureHttpClient(client =>
                {
                    // 可在这里统一配置 HttpClient（如代理、SSL 忽略，生产环境慎用）
                    // client.DefaultRequestHeaders.Add("X-Custom-Header", "luoli-app");
                });

                    //这里是对原始http client logger进行filter
                    builder.Logging.AddFilter(
                "System.Net.Http.HttpClient.LokiHttpClient",
                LogLevel.Warning
            );

            // 注册LokiLogger单例服务（封装日志操作）
            builder.Services.AddSingleton<LuoliCommon.Logger.ILogger, LokiLogger>(provider =>
            {
                var httpClient = provider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("LokiHttpClient");

                var loki = new LokiLogger(Config.KVPairs["LokiEndPoint"],
                    new Dictionary<string, string>(),
                    httpClient);
                loki.AfterLog = (msg) => Console.WriteLine(msg);

                ActionsOperator.Initialize(loki);
                return loki;
            });



            #endregion

            #region 注册sqlsugar
            builder.Services.AddScoped<SqlSugarClient>(provider =>
            {
                var config = new SqlSugar.ConnectionConfig
                {
                    // 从配置文件读取连接字符串
                    ConnectionString = $"server={MasterDB.Host}; port={MasterDB.Port}; user id={MasterDB.User}; password={MasterDB.Password}; database={MasterDB.Database};AllowLoadLocalInfile=true;",
                    DbType = (DbType)MasterDB.DBType,
                    IsAutoCloseConnection = true,

                    InitKeyType = InitKeyType.Attribute, // 通过特性识别主键和自增列

                    SlaveConnectionConfigs = new List<SlaveConnectionConfig>() {
                     new SlaveConnectionConfig() { HitRate=10, ConnectionString= $"server={SlaverDB.Host}; port={SlaverDB.Port}; user id={SlaverDB.User}; password={SlaverDB.Password}; database={SlaverDB.Database};AllowLoadLocalInfile=true;" } ,
                    }
                };

                return new SqlSugarClient(config);
            });

            #endregion

            #region 注册rabbitmq

            // RabbitMQ
            builder.Services.AddSingleton<RabbitMQ.Client.IConnectionFactory>(provider =>
            {
                return new ConnectionFactory
                {
                    HostName = RabbitMQConnection.Host,
                    Port = RabbitMQConnection.Port,
                    UserName = RabbitMQConnection.UserId,
                    Password = RabbitMQConnection.UserId,
                    VirtualHost = "/",
                    // 连接失败时自动重连
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
                };
            });

            // RabbitMQ connection
            builder.Services.AddSingleton<IConnection>(provider =>
            {
                var factory = provider.GetRequiredService<RabbitMQ.Client.IConnectionFactory>();
                return factory.CreateConnectionAsync().Result;
            });

            // RabbitMQ channel
            builder.Services.AddSingleton<IChannel>(provider =>
            {
                var connection = provider.GetRequiredService<IConnection>();
                return connection.CreateChannelAsync().Result;
            });

            builder.Services.AddHostedService<ConsumerService>();

            #endregion

            #region 注册IExternalService

            builder.Services.AddScoped<IExternalOrderService, SqlSugarExternalOrderService>();

            #endregion

            var app = builder.Build();

            ServiceLocator.Initialize(app.Services);

            #region luoli code

            // 应用启动后，通过服务容器获取 LokiLogger 实例
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                try
                {
                    // 获取 LokiLogger 实例
                    var lokiLogger = services.GetRequiredService<LuoliCommon.Logger.ILogger>();

                    // 记录启动日志
                    lokiLogger.Info("应用程序启动成功");
                    lokiLogger.Debug($"环境:{app.Environment.EnvironmentName},端口：{Config.BindAddr}");


                    var assembly = Assembly.GetExecutingAssembly();
                    var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                    var fileVersion = fileVersionInfo.FileVersion;

                    lokiLogger.Info($"CurrentDirectory:[{Environment.CurrentDirectory}]");
                    lokiLogger.Info($"Current File Version:[{fileVersion}]");
                }
                catch (Exception ex)
                {
                    // 启动日志失败时降级输出
                    Console.WriteLine($"启动日志记录失败：{ex.Message}");
                }
            }


            #endregion

            app.MapControllers();

            app.Run(Config.BindAddr);
        }
    }
}
