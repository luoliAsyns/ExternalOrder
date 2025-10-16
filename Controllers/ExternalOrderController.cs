﻿using LuoliCommon.DTO.ExternalOrder;
using LuoliCommon.Entities;
using LuoliCommon.Enums;
using LuoliCommon.Logger;
using LuoliDatabase.Extensions;
using LuoliUtils;
using MethodTimer;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using System.Threading.Channels;
using ILogger = LuoliCommon.Logger.ILogger;

namespace ExternalOrderService.Controllers
{
    public class ExternalOrderController : Controller
    {
        private readonly ILogger _logger;
        private readonly IExternalOrderService _externalOrderService;
        public ExternalOrderController(ILogger logger, 
            IExternalOrderService externalOrderService)
        {
            _logger = logger;
            _externalOrderService = externalOrderService;
        }


        [Time]
        [HttpGet]
        [Route("api/external-order/query")]
        public async Task<ApiResponse<ExternalOrderDTO>> Query(
            [FromQuery] string from_platform,
           [FromQuery] string tid )
        {
            _logger.Info($"trigger ExternalOrderService.Controllers.Query");

            ApiResponse<ExternalOrderDTO> response = new();
            response.code = EResponseCode.Fail;
            response.data = null;

            try
            {
                response = await _externalOrderService.GetAsync(from_platform, tid);
            }
            catch (Exception ex)
            {
                response.msg = ex.Message;
                response.code = EResponseCode.Fail;

                _logger.Error("while ExternalOrderService.Controllers.Query");
                _logger.Error(ex.Message);
            }
            return response;
        }



        [Time]
        [HttpPost]
        [Route("api/external-order/insert")]
        public async Task<ApiResponse<bool>> Insert(
            [FromBody] ExternalOrderDTO dto)
        {
            _logger.Info($"trigger ExternalOrderService.Controllers.Insert");

            ApiResponse<bool> response = new();
            response.code = EResponseCode.Fail;
            response.data = false;

            var (valid, msg) = dto.Validate();
            if (!valid)
            {
                response.msg = msg;
                _logger.Error($"while ExternalOrderService.Controllers.Insert, not passed validate. msg:[{msg}]");
                return response;
            }

            try
            {
                response = await _externalOrderService.InsertAsync(dto);

            }
            catch (Exception ex)
            {
                response.msg = ex.Message;
                response.code = EResponseCode.Fail;

                _logger.Error("while ExternalOrderService.Controllers.Insert");
                _logger.Error(ex.Message);
            }
            return response;
        }

        [Time]
        [HttpPost]
        [Route("api/external-order/delete")]
        public async Task<ApiResponse<bool>> Delete(
            [FromBody] DeleteRequest request)
        {
            _logger.Info($"trigger ExternalOrderService.Controllers.Delete");

            ApiResponse<bool> response = new();
            response.code = EResponseCode.Fail;
            response.data = false;

          
            try
            {
                response = await _externalOrderService.DeleteAsync(request);
            }
            catch (Exception ex)
            {
                response.msg = ex.Message;
                response.code = EResponseCode.Fail;

                _logger.Error("while ExternalOrderService.Controllers.Delete");
                _logger.Error(ex.Message);
            }
            return response;
        }

        [Time]
        [HttpPost]
        [Route("api/external-order/update")]
        public async Task<ApiResponse<bool>> Update(
           [FromBody] ExternalOrderDTO dto)
        {
            _logger.Info($"trigger ExternalOrderService.Controllers.Update");

            ApiResponse<bool> response = new();
            response.code = EResponseCode.Fail;
            response.data = false;

            var (valid, msg) = dto.Validate();
            if (!valid)
            {
                response.msg = msg;
                _logger.Error($"while ExternalOrderService.Controllers.Update, not passed validate. msg:[{msg}]");
                return response;
            }

            try
            {
                response = await _externalOrderService.UpdateAsync(dto);
            }
            catch (Exception ex)
            {
                response.msg = ex.Message;
                response.code = EResponseCode.Fail;

                _logger.Error("while ExternalOrderService.Controllers.Update");
                _logger.Error(ex.Message);
            }
            return response;
        }
    }
}
