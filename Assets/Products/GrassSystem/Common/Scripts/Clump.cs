using System;

namespace Zerls.GrassSystem
{
    [Serializable]
    // =============================================================================================
    // ClumpParameters
    // 作用: 定义一个“草簇类型(Variant)”的形态/朝向/尺寸随机规则，Compute 侧按 Voronoi 纹理映射 index -> 读取该结构，
    //       决定该草实例最终高度、宽度、倾斜、弯曲以及簇聚集/方向一致性。
    // 与 Compute 中 struct ClumpParameters 字段顺序完全一致 (Grass.compute / GrassSystem.cs 上传 stride = 10 * float)。
    // 若添加/删除/调整字段:
    //   1) 同步修改 Grass.compute 中的结构顺序与算法
    //   2) 同步修改 GrassSystem.InitializeComputeBuffers() 中 clumpParametersBuffer stride
    //   3) 重新设置已有列表数据 (旧序列化字段可能失效)
    // 用法: 在 Inspector 的 clumpParameters List 中添加多个配置，再由 Voronoi 簇贴图 (ClumpTex) 中的 R 通道索引选择。
    // 性能提示: 不宜设置过多类型 (数量直接影响 Compute 内分支缓存局部性与数据带宽)；常见 4~12 足够表现差异。
    // 推荐取值范围（可根据美术风格调整）:
    //   pullToCentre:       0~1      (0=完全不聚拢, 1=强力吸向簇中心)
    //   pointInSameDirection:0~1     (0=朝向完全随机, 1=全部统一)
    //   baseHeight:         0.2~2.5  (米, 取决于场景缩放)
    //   heightRandom:       0~1.0    (叠加到 baseHeight * ±heightRandom)
    //   baseWidth:          0.01~0.15
    //   widthRandom:        0~0.1
    //   baseTilt:           -0.6~0.6 (相对高度方向的倾斜比例, 0=垂直)
    //   tiltRandom:         0~0.6
    //   baseBend:           0~0.8    (控制贝塞尔中段偏移, 值大更弯)
    //   bendRandom:         0~0.8
    // 美术调参建议: 先统一 baseHeight / baseWidth 取得整体体量 -> 再通过 pullToCentre 调簇紧凑度 -> 最后逐步添加随机项增强自然度。
    // =============================================================================================
    public struct ClumpParameters
    {
        public float pullToCentre;          // 簇中心收拢强度 (0=不收拢,1=完全吸向)
        public float pointInSameDirection;  // 簇内草朝向一致性 (0=随机,1=完全同向)
        public float baseHeight;            // 基础高度 (米)
        public float heightRandom;          // 高度随机幅度 (乘以 ±1 的扰动后叠加)
        public float baseWidth;             // 基础宽度 (局部 Z 方向一半宽)
        public float widthRandom;           // 宽度随机幅度
        public float baseTilt;              // 顶端倾斜基值 (与高度形成斜向)
        public float tiltRandom;            // 倾斜随机幅度
        public float baseBend;              // 主体弯曲基值 (影响贝塞尔控制点侧向)
        public float bendRandom;            // 弯曲随机幅度
    }
}