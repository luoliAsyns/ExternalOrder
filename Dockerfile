# �����׶�
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release

# ���ù���Ŀ¼Ϊ��Ŀ��Ŀ¼��Dockerfile����Ŀ¼��
WORKDIR /src



# ����CommonĿ¼�������������·���������ڣ�
# ע�⣺�����../..�������Dockerfile��λ��
COPY Common ./Common/
COPY ExternalOrderService ./ExternalOrderService/

# ȷ������Ŀ¼��ȷָ����Ŀ�ļ�
WORKDIR "/src/ExternalOrderService"

# �Ȼ�ԭ������ȷ�����ҵ�Common��Ŀ
RUN dotnet restore "./ExternalOrderService/ExternalOrderService.csproj"

# ������Ŀ
RUN dotnet build "./ExternalOrderService/ExternalOrderService.csproj" -c $BUILD_CONFIGURATION -o /app/build

# �����׶�
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ExternalOrderService/ExternalOrderService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ���н׶�
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ExternalOrderService.dll"]
