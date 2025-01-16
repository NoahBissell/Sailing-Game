using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BitVisual : MonoBehaviour
{
    // Start is called before the first frame update

    public int possibleValues;
    public int x;
    public Color xColor;
    public int y;
    public Color yColor;
    public Color resColor;
    public SpriteRenderer valuePrefab;
    public float height = 1;

    private SpriteRenderer[] _points;

    private float _dst;
    private float _leftBound;

    private bool _started = false;
    
    void Start()
    {
        _leftBound = -Camera.main.orthographicSize;
        _dst = - _leftBound * 2 / possibleValues;
        _points = new SpriteRenderer[possibleValues];

        for (int i = 0; i < possibleValues; i++)
        {
            SpriteRenderer s = Instantiate(valuePrefab, transform, true);
            s.transform.position = new Vector2(_leftBound + _dst * i, 0);
            s.transform.localScale *= 2 * _dst;
            s.transform.localScale = new Vector2(s.transform.localScale.x, height);
            _points[i] = s;
        }

        _started = true;
    }

    private void OnValidate()
    {
        if (!_started) return;

        ResetColors();
        
        int res = x ^ y;
        if (res >= possibleValues)
        {
            print("Outside bounds: " + res);
            return;
        }
        _points[x].color = xColor;
        _points[y].color = yColor;
        _points[res].color = resColor;
    }

    void ResetColors()
    {
        for (int i = 0; i < possibleValues; i++)
        {
            _points[i].color = Color.white;
        }
    }
}
