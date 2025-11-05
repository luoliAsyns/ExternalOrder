using Azure.Core;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LuoliCommon.DTO.Coupon;
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


        public async Task<ApiResponse<bool>> InsertAsync(ExternalOrderDTO dto)
        {
            var result = new ApiResponse<bool>();
            result.code= EResponseCode.Fail;
            result.data = false;

            try
            {
                _logger.Info($"InsertAsync BeginTran with ExternalOrderDTO.Tid:[{dto.Tid}]");

                await _sqlClient.BeginTranAsync();
                var entity = dto.ToEntity();

                await _sqlClient.Insertable(entity).ExecuteCommandAsync();
                await _sqlClient.CommitTranAsync();

                _logger.Info($"InsertAsync commit success with ExternalOrderDTO.Tid:[{dto.Tid}]");

                result.code = EResponseCode.Success;
                result.data = true;


                await _channel.BasicPublishAsync(exchange: string.Empty,
                    routingKey: Program.Config.KVPairs["StartWith"] + RabbitMQKeys.ExternalOrderInserted, 
                    true,
                    _rabbitMQMsgProps,
                   Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto)));

                
                _logger.Info($"SqlSugarExternalOrderService.InsertAsync sent ExternalOrderDTO to MQ [{Program.Config.KVPairs["StartWith"] + RabbitMQKeys.ExternalOrderInserted}] with ExternalOrderDTO.Tid:[{dto.Tid}]");
                
            }
            catch (Exception ex)
            {
                result.msg = ex.Message;
                await _sqlClient.RollbackTranAsync();
                _logger.Error($"while SqlSugarExternalOrderService.InsertAsync, rollback with ExternalOrderDTO.Tid:[{dto.Tid}]");
                _logger.Error(ex.Message);
            }

            return result;
        }

        public async Task<ApiResponse<bool>> DeleteAsync(DeleteRequest request)
        {
            var redisKey = $"externalorder.{request.from_platform}.{request.tid}";

            var result = new ApiResponse<bool>();
            result.code = EResponseCode.Fail;
            result.data = false;

            try
            {
                _logger.Info($"DeleteAsync BeginTran with ExternalOrderDTO.Tid:[{request.tid}]");

                await _sqlClient.BeginTranAsync();
                int impactRows= await _sqlClient.Updateable<object>()
                    .AS("external_order")
                    .SetColumns("is_deleted", true)
                    .Where($"from_platform='{request.from_platform}' and tid='{request.tid}'").ExecuteCommandAsync();

                if (impactRows != 1)
                    throw new Exception("SqlSugarExternalOrderService.DeleteAsync impactRows not equal to 1");

                await _sqlClient.CommitTranAsync();

                _logger.Info($"DeleteAsync commit success with ExternalOrderDTO.Tid:[{request.tid}]");


                result.code = EResponseCode.Success;
                result.data = true;

                RedisHelper.DelAsync(redisKey);

                _logger.Info($"SqlSugarExternalOrderService.DeleteAsync success with ExternalOrderDTO.Tid:[{request.tid}], remove cache");

            }
            catch (Exception ex)
            {
                result.msg = ex.Message;
                await _sqlClient.RollbackTranAsync();
                _logger.Error($"while SqlSugarExternalOrderService.DeleteAsync with ExternalOrderDTO.Tid:[{request.tid}]");
                _logger.Error(ex.Message);
            }

            return result;
        }

        public async Task<ApiResponse<ExternalOrderDTO>> GetAsync(string fromPlatform, string Tid)
        {
            var result = new ApiResponse<ExternalOrderDTO>();
            result.code = EResponseCode.Fail;
            result.data = null;

            try
            {
                var redisKey = $"externalorder.{fromPlatform}.{Tid}";
                var externalOrder =await RedisHelper.GetAsync<ExternalOrderEntity>(redisKey);

                if(!(externalOrder is null))
                {
                    _logger.Info($"cache hit for key:[{redisKey}]");
                    result.code = EResponseCode.Success;
                    result.data = externalOrder.ToDTO();
                    result.msg = "from redis";
                    return result;
                }

                _logger.Info($"cache miss for key:[{redisKey}]");

                externalOrder = await _sqlClient.Queryable<ExternalOrderEntity>()
                    .Where(o=>o.tid == Tid && o.from_platform == fromPlatform && o.is_deleted == 0).FirstAsync();
                
                result.code = EResponseCode.Success;
                result.data = externalOrder.ToDTO();
                result.msg = "from database";

                if (result.data is null)
                    _logger.Warn($"SqlSugarExternalOrderService.GetAsync success with Tid:[{Tid}], but data is null");
                else
                {
                    RedisHelper.SetAsync(redisKey, externalOrder, 60);
                    _logger.Info($"SqlSugarExternalOrderService.GetAsync success with Tid:[{Tid}], add it into cache");
                }

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
            var result = new ApiResponse<bool>();
            result.code = EResponseCode.Fail;
            result.data = false;

            try
            {
                var redisKey = $"externalorder.{dto.FromPlatform}.{dto.Tid}";

                _logger.Info($"UpdateAsync BeginTran with ExternalOrderDTO.Tid:[{dto.Tid}]");

                await _sqlClient.BeginTranAsync();
                int impactRows = await _sqlClient.Updateable(dto.ToEntity())
                     .Where($"from_platform='{dto.FromPlatform}' and tid='{dto.Tid}' and is_deleted='0'")
                     .IgnoreColumns(it=> new {it.tid, it.from_platform})
                     .ExecuteCommandAsync();
                if (impactRows != 1)
                    throw new Exception("SqlSugarExternalOrderService.UpdateAsync impactRows not equal to 1");

                await _sqlClient.CommitTranAsync();
                
                _logger.Info($"UpdateAsync commit success with ExternalOrderDTO.Tid:[{dto.Tid}]");

                result.code = EResponseCode.Success;
                result.data = true;

                RedisHelper.DelAsync(redisKey);

                _logger.Info($"SqlSugarExternalOrderService.UpdateAsync success with Tid:[{dto.Tid}], remove cache");

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

        public async Task<ApiResponse<PageResult<ExternalOrderDTO>>> PageQueryAsync(int page = 1, int size = 10, 
            DateTime? startTime = null, DateTime? endTime = null)
        {
            ApiResponse<PageResult<ExternalOrderDTO>> response = new();

            response.code = EResponseCode.Fail;
            response.data = null;

            try
            {
                var query = _sqlClient
                    .Queryable<ExternalOrderEntity>()
                    .Where(o => o.is_deleted == 0); // 排除已删除的订单

                // 动态添加筛选条件

                if (startTime.HasValue)
                    query = query.Where(o => o.create_time >= startTime.Value);

                if (endTime.HasValue)
                    query = query.Where(o => o.create_time <= endTime.Value);

                // 4. 执行分页查询（先查总数，再查当前页数据）
                long total = await query.CountAsync();
                List<ExternalOrderEntity> eos = await query
                    .OrderByDescending(o => o.create_time) // 按创建时间倒序
                    .Skip((page - 1) * size)
                    .Take(size)
                    .ToListAsync();

                // 4. 构建分页结果
                var pageResult = new PageResult<ExternalOrderDTO>
                {
                    Total = total,       // 总记录数
                    Page = page,         // 当前页码
                    Size = size,         // 每页大小
                    Items = eos.Select(entity => entity.ToDTO()).ToList()      // 当前页数据列表
                };

                // 5. 返回统一响应格式
                response.data = pageResult;
                response.msg = "success";
                response.code = EResponseCode.Success;
            }
            catch (Exception ex)
            {
                response.msg = ex.Message;
                _logger.Error($"while SqlSugarExternalOrderService.PageQueryAsync with page:[{page}] size:[{size}]");
                _logger.Error(ex.Message);
            }
            return response;
        }
    }
}
