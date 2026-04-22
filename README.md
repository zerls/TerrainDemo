# TerrainDemo

一个Unity大世界展示Demo，展示了GPU驱动的地形渲染、动态草系统、大气散射和云渲染等技术。

## 功能特性

### 🏔️ GPU驱动地形渲染系统
- **四叉树LOD (Level of Detail)**: 动态调整地形细节层次，根据距离自动切换LOD级别
- **视锥剔除 (Frustum Culling)**: 高效剔除视野外的地形块
- **Hiz遮挡剔除 (Hierarchical Z Occlusion Culling)**: 使用深度缓冲进行遮挡剔除，提高渲染性能
- **无缝拼接 (Seamless Tiling)**: 确保相邻地形块之间的无缝过渡

### 🌱 动态草系统
- **多LOD级别**: 支持多个细节层次的草渲染
- **密度控制**: 可调节全局草密度和抖动强度
- **距离淡化**: 根据距离渐进式减少草密度
- **视锥剔除**: 高效剔除视野外的草簇
- **控制贴图**: 使用纹理控制草的生长区域

### ☁️ 天空与大气渲染
- **大气散射**: 物理基础的大气散射模拟
- **体积云**: 实时体积云渲染，支持多种噪声纹理
- **动态光照**: 与Unity光照系统集成
- **天气纹理**: 支持天气影响的云层变化

### 🌪️ 风模拟
- **GPU加速**: 使用Compute Shader进行风力场计算
- **实时交互**: 风力影响草和树叶的动画

## 系统要求

- **Unity版本**: 2022.3.62f3 或更高版本
- **渲染管线**: Universal Render Pipeline (URP) 14.0.12
- **平台支持**: Windows, macOS, Linux
- **硬件要求**:
  - 支持Compute Shader的GPU
  - 推荐至少4GB VRAM

## 安装与运行

### 1. 克隆项目
```bash
git clone <repository-url>
cd TerrainDemo
```

### 2. 打开Unity项目
- 使用Unity 2022.3.62f3或兼容版本打开项目
- Unity会自动安装所需的包依赖

### 3. 运行演示场景
- 在Unity编辑器中打开 `Assets/Products/MainTerrainDemo.unity` 场景
- 点击播放按钮运行演示

### 4. 其他演示场景
- `Atmospheric_m.unity`: 大气散射演示
- `VolumetricCloud.unity`: 体积云演示
- `GrassMesh_NoiseTexture.unity`: 草系统演示
- `Voronoi.unity`: 沃罗诺伊图演示

## 项目结构

```
Assets/
├── Core/                    # 核心渲染组件
├── Products/               # 产品功能模块
│   ├── TerrainRendering/   # 地形渲染系统
│   │   ├── Scripts/        # 地形管理脚本
│   │   ├── Shaders/        # 地形着色器
│   │   └── Art/           # 地形资源
│   ├── GrassSystem/        # 草系统
│   │   ├── Scripts/        # 草系统脚本
│   │   ├── Shaders/        # 草着色器
│   │   └── Common/         # 共享资源
│   ├── SkyRendering/       # 天空渲染
│   │   ├── Scripts/        # 天空管理脚本
│   │   ├── Shaders/        # 天空着色器
│   │   └── AtmosphericScattering/  # 大气散射
│   ├── WindSimulation/     # 风模拟
│   └── Common/             # 共享组件
└── Settings/               # 项目设置
```

## 核心组件说明

### TerrainManager.cs
地形渲染系统的核心管理器，负责：
- 初始化GPU缓冲区和Compute Shader
- 执行四叉树遍历和LOD计算
- 进行视锥和遮挡剔除
- 生成渲染指令

### GrassSystem_lod.cs
草系统的LOD管理器，提供：
- 多级别LOD渲染
- 密度和距离控制
- GPU驱动的实例化渲染

### SkyManager.cs
天空渲染管理器，集成：
- 大气散射计算
- 云层渲染
- 光照参数管理

## 性能优化

- **GPU驱动渲染**: 大部分计算在GPU上进行，减少CPU负载
- **LOD系统**: 根据距离动态调整细节级别
- **剔除技术**: 视锥剔除和遮挡剔除减少绘制调用
- **实例化渲染**: 高效渲染大量草和植被实例

## 自定义配置

### 地形设置
在 `TerrainManager` 组件中调整：
- `HeightOffset`: 地形高度偏移
- `distanceEvaluation`: LOD距离评估参数
- `hizDepthBias`: Hiz遮挡深度偏移

### 草系统设置
在 `GrassSystem_lod` 组件中配置：
- `globalDensity`: 全局草密度
- `maxRenderDistance`: 最大渲染距离
- `lodSettings`: 各LOD级别的设置

### 天空设置
在 `SkyManager` 组件中调整：
- `atmosphereSettings`: 大气参数
- `cloudSettings`: 云层参数
- `CloudDensityThreshold`: 云密度阈值

## 开发与贡献


## 许可证

本项目采用MIT许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 致谢

- Unity Technologies - 提供强大的游戏引擎
- 感谢开源社区的Compute Shader和渲染技术贡献

## 联系方式

如有问题或建议，请通过以下方式联系：
- 邮箱: starsss747@gmail.com
- 项目主页: http://ta.zerl.top/

---

**注意**: 此项目需要现代GPU支持Compute Shader。如遇到渲染问题，请确保显卡驱动是最新的。</content>