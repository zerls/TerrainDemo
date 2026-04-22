#ifndef TERRAIN_COMMON_HLSL
#define TERRAIN_COMMON_HLSL

#define MAX_TERRAIN_LOD 5           //最大的LOD级别是5
#define MAX_LOD_NODE_COUNT 5
#define MAX_NODE_ID 34124           //5x5+10x10+20x20+40x40+80x80+160x160 - 1,最大Node数量
#define PATCH_MESH_GRID_COUNT 16    //一个PatchMesh由16x16网格组成
#define PATCH_MESH_SIZE 8           //一个PatchMesh边长8米
#define PATCH_COUNT_PER_NODE 8      //一个Node拆成8x8个Patch
#define PATCH_MESH_GRID_SIZE 0.5    //PatchMesh一个格子的大小为0.5x0.5
#define SECTOR_COUNT_WORLD 160

struct NodeDescriptor
{
    uint b_divide;
};

struct PatchDescriptor
{
    float2 position;
    float2 minMaxHeight;
    uint lod;
    uint lodTransPacked; 
};

struct Bounds
{
    float3 minPosition;
    float3 maxPosition;
};

inline uint PackLodTrans(uint4 lodTrans)
{
    return (lodTrans.x & 0xFF) | ((lodTrans.y & 0xFF) << 8) | ((lodTrans.z & 0xFF) << 16) | ((lodTrans.w & 0xFF) << 24);
}

inline uint4 UnpackLodTrans(uint packedTrans)
{
    return uint4(
        packedTrans & 0xFF,
        (packedTrans >> 8) & 0xFF,
        (packedTrans >> 16) & 0xFF,
        (packedTrans >> 24) & 0xFF
    );
}

#endif
