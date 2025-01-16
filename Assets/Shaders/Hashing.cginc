int UnsignedRightShift(int s, int i)
{
    return (uint)s >> i;
}

int2 get_quantized_coord(float2 position, float cell_size)
{
    return (int2)(floor(position / cell_size));
}

int hash_position(int2 coord, uint num)
{
    int x = (coord.x * (coord.x < 0 ? 786433 : 196613));
    int y = (coord.y * (coord.y < 0 ? 100663319 : 12582917));

    return UnsignedRightShift(((3145739 + x) * 25165843 + y), 1) % num;
}
    
int hash_position(float2 position, float cell_size, uint num)
{
    return hash_position(get_quantized_coord(position, cell_size), num);
}