# 构建阶段
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release

# 设置工作目录为项目根目录（Dockerfile所在目录）
WORKDIR /src



# 复制Common目录（从宿主机相对路径到容器内）
# 注意：这里的../..是相对于Dockerfile的位置
COPY Common ./Common/
COPY ExternalOrderService ./ExternalOrderService/

# 确保工作目录正确指向项目文件
WORKDIR "/src/ExternalOrderService"

# 先还原依赖，确保能找到Common项目
RUN dotnet restore "./ExternalOrderService/ExternalOrderService.csproj"

# 构建项目
RUN dotnet build "./ExternalOrderService/ExternalOrderService.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 发布阶段
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ExternalOrderService/ExternalOrderService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# 运行阶段
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ExternalOrderService.dll"]
