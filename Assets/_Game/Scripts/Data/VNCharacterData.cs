using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "VN/Character Data")]
public class VNCharacterData : ScriptableObject
{
    public string characterId;
    public string displayName;
    public Color nameColor = new Color(0.46f, 0.66f, 0.98f);

    [Header("Typewriter Voice Blip")]
    public AudioClip voiceBlip;
    [Range(0.5f, 1.5f)] public float basePitch = 1.0f;
    [Range(0f, 0.2f)] public float pitchVariance = 0.04f;

    [Serializable]
    public struct ExpressionEntry
    {
        public string key;
        public Sprite sprite;
    }

    public List<ExpressionEntry> expressions = new();
    private Dictionary<string, Sprite> _lookup;

    public Sprite GetExpression(string key)
    {
        if (_lookup == null)
        {
            _lookup = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in expressions)
                _lookup[entry.key] = entry.sprite;
        }

        if (_lookup.TryGetValue(key, out var sprite))
            return sprite;

        return expressions.Count > 0 ? expressions[0].sprite : null;
    }
}