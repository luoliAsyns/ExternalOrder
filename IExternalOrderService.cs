using LuoliCommon.DTO.ExternalOrder;
using LuoliCommon.Entities;
using MethodTimer;

namespace ExternalOrderService
{

    public interface IExternalOrderService
    {
        [Time]
        Task<ApiResponse<bool>> InsertAsync(ExternalOrderDTO dto);

        [Time]
        Task<ApiResponse<ExternalOrderDTO>> GetAsync(string fromPlatform, string Tid);

        [Time]
        Task<ApiResponse<bool>> UpdateAsync(ExternalOrderDTO dto);

        [Time]
        Task<ApiResponse<bool>> DeleteAsync(DeleteRequest request);


    }
}
