using Azure.Core;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LuoliCommon.DTO.ExternalOrder;
using LuoliCommon.Entities;
using LuoliCommon.Enums;
using LuoliCommon.Logger;
using LuoliDatabase;
using LuoliDatabase.Entities;
using LuoliDatabase.Extensions;
using LuoliUtils;
using MethodTimer;
using RabbitMQ.Client;
using SqlSugar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ILogger = LuoliCommon.Logger.ILogger;

namespace ExternalOrderService
{
    // 实现服务接口
    public class SqlSugarExternalOrderService : IExternalOrderService
    {
        // 注入的依赖项
        private readonly ILogger _logger;
        private readonly SqlSugarClient _sqlClient;
        private readonly IChannel _channel;

        // 构造函数注入
        public SqlSugarExternalOrderService( ILogger logger, SqlSugarClient sqlClient,
             IChannel channel)
        {
            _logger = logger;
            _sqlClient = sqlClient;
            _channel = channel;

            _rabbitMQMsgProps.ContentType = "text/plain";
            _rabbitMQMsgProps.DeliveryMode = DeliveryModes.Persistent;
        }

        private static BasicProperties _rabbitMQMsgProps = new BasicProperties();


        [Time]
        public async Task<ApiResponse<bool>> InsertAsync(ExternalOrderDTO dto)
        {
            _logger.Debug("starting SqlSugarExternalOrderService.InsertAsync ");
            var result = new ApiResponse<bool>();
            result.code= EResponseCode.Fail;
            result.data = false;

            try
            {
                await _sqlClient.BeginTranAsync();
                var entity = dto.ToEntity();

                await _sqlClient.Insertable(entity).ExecuteCommandAsync();
                await _sqlClient.CommitTranAsync();

                result.code = EResponseCode.Success;
                result.data = true;


                await _channel.BasicPublishAsync(exchange: string.Empty,
                    routingKey: Program.Config.KVPairs["StartWith"] + RabbitMQKeys.ExternalOrderInserted, 
                    true,
                    _rabbitMQMsgProps,
                   Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto)));

                
                _logger.Debug($"SqlSugarExternalOrderService.InsertAsync success with dto.Tid:[{dto.Tid}]");
                
            }
            catch (Exception ex)
            {
                result.msg = ex.Message;
                await _sqlClient.RollbackTranAsync();
                _logger.Error($"while SqlSugarExternalOrderService.InsertAsync with dto.Tid:[{dto.Tid}]");
                _logger.Error(ex.Message);
            }

            return result;
        }

        public async Task<ApiResponse<bool>> DeleteAsync(DeleteRequest request)
        {
            _logger.Debug("starting SqlSugarExternalOrderService.DeleteAsync ");

            var redisKey = $"externalorder.{request.from_platform}.{request.tid}";

            var result = new ApiResponse<bool>();
            result.code = EResponseCode.Fail;
            result.data = false;

            try
            {
                await _sqlClient.BeginTranAsync();
               int impactRows= await _sqlClient.Updateable<object>()
                .AS("external_order")
                .SetColumns("is_deleted", true)
                .Where($"from_platform='{request.from_platform}' and tid='{request.tid}'").ExecuteCommandAsync();
                await _sqlClient.CommitTranAsync();
                if (impactRows != 1)
                    throw new Exception("SqlSugarExternalOrderService.DeleteAsync impactRows not equal to 1");

                result.code = EResponseCode.Success;
                result.data = true;
                _logger.Debug($"SqlSugarExternalOrderService.DeleteAsync success with Tid:[{request.tid}]");

                RedisHelper.DelAsync(redisKey);

            }
            catch (Exception ex)
            {
                result.msg = ex.Message;
                await _sqlClient.RollbackTranAsync();
                _logger.Error($"while SqlSugarExternalOrderService.DeleteAsync with Tid:[{request.tid}]");
                _logger.Error(ex.Message);
            }

            return result;
        }


        public async Task<ApiResponse<ExternalOrderDTO>> GetAsync(string fromPlatform, string Tid)
        {
            _logger.Debug("starting SqlSugarExternalOrderService.GetAsync ");
            var result = new ApiResponse<ExternalOrderDTO>();
            result.code = EResponseCode.Fail;
            result.data = null;

            try
            {
                var redisKey = $"externalorder.{fromPlatform}.{Tid}";
                var externalOrder = RedisHelper.Get<ExternalOrderEntity>(redisKey);

                if(!(externalOrder is null))
                {
                    result.code = EResponseCode.Success;
                    result.data = externalOrder.ToDTO();
                    result.msg = "from redis";
                    return result;
                }

                externalOrder= await _sqlClient.Queryable<ExternalOrderEntity>()
                    .Where(o=>o.tid == Tid && o.from_platform == fromPlatform && o.is_deleted == 0).FirstAsync();
              
               
                result.code = EResponseCode.Success;
                result.data = externalOrder.ToDTO();
                result.msg = "from database";

                if (!(result.data is null))
                    RedisHelper.SetAsync(redisKey, externalOrder, 60);

                _logger.Debug($"SqlSugarExternalOrderService.GetAsync success with Tid:[{Tid}]");

            }
            catch (Exception ex)
            {
                result.msg = ex.Message;
                _logger.Error($"while SqlSugarExternalOrderService.GetAsync with Tid:[{Tid}]");
                _logger.Error(ex.Message);
            }

            return result;
        }



        public async Task<ApiResponse<bool>> UpdateAsync(ExternalOrderDTO dto)
        {
            _logger.Debug("starting SqlSugarExternalOrderService.UpdateAsync ");
            var result = new ApiResponse<bool>();
            result.code = EResponseCode.Fail;
            result.data = false;

            try
            {
                var redisKey = $"externalorder.{dto.FromPlatform}.{dto.Tid}";

                await _sqlClient.BeginTranAsync();
                int impactRows = await _sqlClient.Updateable(dto.ToEntity())
                 .Where($"from_platform='{dto.FromPlatform}' and tid='{dto.Tid}' and is_deleted='0'")
                 .IgnoreColumns(it=> new {it.tid, it.from_platform}).ExecuteCommandAsync();
                await _sqlClient.CommitTranAsync();
                if (impactRows != 1)
                    throw new Exception("SqlSugarExternalOrderService.UpdateAsync impactRows not equal to 1");

                result.code = EResponseCode.Success;
                result.data = true;

                _logger.Debug($"SqlSugarExternalOrderService.UpdateAsync success with Tid:[{dto.Tid}]");

                RedisHelper.DelAsync(redisKey);
            }
            catch (Exception ex)
            {
                result.msg = ex.Message;
                await _sqlClient.RollbackTranAsync();
                _logger.Error($"while SqlSugarExternalOrderService.UpdateAsync with Tid:[{dto.Tid}]");
                _logger.Error(ex.Message);
            }

            return result;
        }

        
    }
}
