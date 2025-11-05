using LuoliCommon.DTO.ExternalOrder;
using LuoliCommon.Entities;
using MethodTimer;

namespace ExternalOrderService
{

    public interface IExternalOrderService
    {
        Task<ApiResponse<bool>> InsertAsync(ExternalOrderDTO dto);
        Task<ApiResponse<ExternalOrderDTO>> GetAsync(string fromPlatform, string Tid);

        Task<ApiResponse<PageResult<ExternalOrderDTO>>> PageQueryAsync(int page = 1,
            int size = 10,
            DateTime? startTime = null,
            DateTime? endTime = null);
        Task<ApiResponse<bool>> UpdateAsync(ExternalOrderDTO dto);
        Task<ApiResponse<bool>> DeleteAsync(DeleteRequest request);


    }
}
