using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using System.Linq;
using UnityEngine.Windows;
using Object = System.Object;

public class SpatialLookup : UnityEngine.Object
{
    private static readonly int CompOffsetID = Shader.PropertyToID("comp_offset");
    private static readonly int GroupSizeID = Shader.PropertyToID("group_size");
    private ComputeShader _sortCompute;

    private int2[] _compIndices;
    private int2[] _spatialLookup;
    private int2[] _cIndexRanges;

    private int _numParticles;
    private int _numParticlesPwr2;
    private ComputeBuffer _positionBuffer;
    private ComputeBuffer _compIndicesBuffer;
    private ComputeBuffer _spatialBuffer;
    private ComputeBuffer _cIndexRangeBuffer;

    private int _hashKernel;
    private int _swapKernel;
    private int _rangeKernel;

    private int _numPhases;
   
    private int _numComparators;
    private int _numComparatorsPwr2;
    
    public SpatialLookup(int numParticles, float cellSize, ComputeBuffer positionBuffer, ComputeBuffer spatialBuffer, ComputeBuffer cIndexRangeBuffer)
    {
        _numParticles = numParticles;
        _numComparators = Mathf.CeilToInt(numParticles / 2f);

        _numParticlesPwr2 = Mathf.NextPowerOfTwo(_numParticles);
        _numComparatorsPwr2 = Mathf.NextPowerOfTwo(_numComparators);
        
        _numPhases = LogBase2((uint)_numParticlesPwr2);
        
        _positionBuffer = positionBuffer;
        _cIndexRangeBuffer = cIndexRangeBuffer;
        _spatialBuffer = spatialBuffer;
        _compIndicesBuffer = new ComputeBuffer(_numComparatorsPwr2, sizeof(int) * 2);
        
        _sortCompute = (ComputeShader)Instantiate(Resources.Load ("SortCompute"));        
        _hashKernel = _sortCompute.FindKernel("HashPositions");
        _swapKernel = _sortCompute.FindKernel("BitonicSwaps");
        _rangeKernel = _sortCompute.FindKernel("GetRanges");
        
        _sortCompute.SetInt("num_particles", numParticles);
        _sortCompute.SetFloat("num_comparators", _numComparators);
        _sortCompute.SetFloat("cell_size", cellSize);
        
        _sortCompute.SetBuffer(_hashKernel, "positions", _positionBuffer);
        
        _sortCompute.SetBuffer(_swapKernel, "comp_indices", _compIndicesBuffer);

        _sortCompute.SetBuffer(_hashKernel, "spatial_lookup", spatialBuffer);
        _sortCompute.SetBuffer(_swapKernel, "spatial_lookup", spatialBuffer);
        _sortCompute.SetBuffer(_rangeKernel, "spatial_lookup", spatialBuffer);
        
        _sortCompute.SetBuffer(_hashKernel, "c_index_ranges", cIndexRangeBuffer);
        _sortCompute.SetBuffer(_swapKernel, "c_index_ranges", cIndexRangeBuffer);
        _sortCompute.SetBuffer(_rangeKernel, "c_index_ranges", cIndexRangeBuffer);
        
        _spatialLookup = new int2[numParticles];
        _cIndexRanges = new int2[numParticles];
        _compIndices = new int2[_numComparatorsPwr2];
        
    }

    public void Destroy()
    {
        _compIndicesBuffer.Release();
        Destroy(this);
    }

    private int LogBase2(uint n)
    {
        int size = 0;
        while (n != 1)
        {
            size++;
            n >>= 1;
        }
        return size;
    }

    static int UnsignedRightShift(int s, int i)
    {
        int y = (int)((uint)s >> i);
        return y;
    }
    
    int2 GetQuantizedCoord(float2 position, float cellSize)
    {
        return (int2)(math.floor(position / cellSize));
    }
    
    int HashPosition(int2 coord, int num)
    {
        int x = (coord.x * (coord.x < 0 ? 786433 : 196613));
        int y = (coord.y * (coord.y < 0 ? 100663319 : 12582917));
        
        return UnsignedRightShift(((3145739 + x) * 25165843 + y), 1) % num;
    }
    
    int HashPosition(float2 position, float cellSize, int num)
    {
        return HashPosition(GetQuantizedCoord(position, cellSize), num);
    }
    
   
    public void UpdateSpatialLookup(float2[] positions, float cellSize)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            _spatialLookup[i] = new(i, HashPosition(positions[i], cellSize, positions.Length));
            _cIndexRanges[i] = int2.zero;
        }
        
        Array.Sort(_spatialLookup, (p1, p2) => p2.y - p1.y);

        int last = _spatialLookup[0].y;
        _cIndexRanges[_spatialLookup[0].y].x = 0;

        for (int i = 1; i < _spatialLookup.Length; i++)
        {
            if (_spatialLookup[i].y == last) continue;

            _cIndexRanges[_spatialLookup[i - 1].y].y = i;
            _cIndexRanges[_spatialLookup[i].y].x = i;
            last = _spatialLookup[i].y;
        }
        
        _cIndexRanges[_spatialLookup[^1].y].y = _spatialLookup.Length;
        
        _cIndexRangeBuffer.SetData(_cIndexRanges);
        _spatialBuffer.SetData(_spatialLookup);
    }

    

    public void UpdateSpatialLookupGPUSlow()
    {
        _sortCompute.Dispatch(_hashKernel, _numParticles / 64 + 1, 1, 1);
        
        for(int phaseIndex = 0; phaseIndex < _numPhases; phaseIndex++)
        {
            int groupSize = 1 << (phaseIndex + 1);
            int numGroups = _numParticlesPwr2 >> (phaseIndex + 1);
            int numCompsPerGroup = groupSize >> 1;
            for (int groupIndex = 0; groupIndex < numGroups; groupIndex++)
            {
                int groupPStartIndex = groupIndex * groupSize;
                int groupPEndIndex = groupPStartIndex + groupSize - 1;
                
                for (int swapIndex = 0; swapIndex < numCompsPerGroup; swapIndex++)
                {
                    int2 comp = new int2(groupPStartIndex + swapIndex, groupPEndIndex - swapIndex);
                    int compIndex = groupIndex * numCompsPerGroup + swapIndex;
                    _compIndices[compIndex] = comp;
                }
            }
            _compIndicesBuffer.SetData(_compIndices);
            _sortCompute.Dispatch(_swapKernel, _numComparatorsPwr2 / 32, 1, 1);

            for (int bitonicStageIndex = 0; bitonicStageIndex < phaseIndex; bitonicStageIndex++)
            {
                int bitonicGroupSize = groupSize >> (bitonicStageIndex + 1);
                int numBitonicGroups = 1 << (bitonicStageIndex + 1);
                for (int groupIndex = 0; groupIndex < numGroups; groupIndex++)
                {
                    int groupPStartIndex = groupIndex * groupSize;
                    
                    for (int bitonicGroupIndex = 0; bitonicGroupIndex < numBitonicGroups; bitonicGroupIndex++)
                    {
                        int bitonicGroupPStartIndex = groupPStartIndex + bitonicGroupIndex * bitonicGroupSize;
                        int bitonicCompSpan = bitonicGroupSize >> 1;
                        int numBitonicCompsPerGroup = bitonicGroupSize >> 1;
                        for (int bitonicSwapIndex = 0; bitonicSwapIndex < numBitonicCompsPerGroup; bitonicSwapIndex++)
                        {
                            int2 comp = new int2(bitonicGroupPStartIndex + bitonicSwapIndex,
                                bitonicGroupPStartIndex + bitonicSwapIndex + bitonicCompSpan);
                            int compIndex = groupIndex * numCompsPerGroup +
                                            bitonicGroupIndex * numBitonicCompsPerGroup + bitonicSwapIndex;
                            _compIndices[compIndex] = comp;
                        }
                    }
                }
                _compIndicesBuffer.SetData(_compIndices);
                _sortCompute.Dispatch(_swapKernel, _numComparatorsPwr2 / 32, 1, 1);
            }
        }
        _sortCompute.Dispatch(_rangeKernel, _numParticles / 64 + 1, 1, 1);
        
    }

    public void UpdateSpatialLookupGPU()
    {
        _sortCompute.Dispatch(_hashKernel, _numParticles / 64 + 1, 1, 1);
        for (int groupSize = 2; groupSize <= _numParticlesPwr2; groupSize <<= 1)
        {
            for (int compOffset = groupSize >> 1; compOffset > 0; compOffset >>= 1)
            {
                _sortCompute.SetInt(CompOffsetID, compOffset);
                _sortCompute.SetInt(GroupSizeID, groupSize);
                _sortCompute.Dispatch(_swapKernel, _numParticles / 64 + 1, 1, 1);
            }
        }
        _sortCompute.Dispatch(_rangeKernel, _numParticles / 64 + 1, 1, 1);
    }

    public void HashPositions(float2[] positions)
    {
        _positionBuffer.SetData(positions);
        _sortCompute.Dispatch(_hashKernel, _numParticles / 64, 1, 1);
    }

}