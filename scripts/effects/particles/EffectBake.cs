using System;
using Godot;

/// <summary>
/// Запечённый эффект: сцена вспышки, взрыва или пыли, приведённая к перечню вбросов
/// и отпечатков.
///
/// ЗАЧЕМ ЗАПЕКАНИЕ ЗДЕСЬ, КАК И У МОДЕЛИ. Сцена эффекта удобна художнику вложенными узлами
/// с полным разбором в инспекторе, а игре от неё нужны лишь числа: сколько частиц выпустить,
/// откуда, с какой скоростью и в какое общее поле. Разбор проходит один раз на сцену,
/// после чего ни узлов эффекта, ни их экземпляров в мире не остаётся вовсе — см.
/// <see cref="ParticleBake"/>.
///
/// СЦЕНА ОСТАЁТСЯ ИСТОЧНИКОМ ПРАВДЫ. Художник по-прежнему правит узлы <see cref="GpuParticles2D"/>
/// и видит их предпросмотр в редакторе; запекание лишь читает то, что он настроил.
/// </summary>
public sealed class EffectBake
{
    /// <summary>Части эффекта — по одной на каждый узел частиц исходной сцены.</summary>
    public EmissionProfile[] Parts = Array.Empty<EmissionProfile>();

    /// <summary>Отметины копоти, объявленные в сцене. Пусто — эффект следа не оставляет.</summary>
    public StampProfile[] Stamps = Array.Empty<StampProfile>();

    /// <summary>
    /// Наибольший срок жизни среди частей. Нужен ведущему потока: после остановки носителя
    /// пыль ещё висит в воздухе именно столько.
    /// </summary>
    public float Duration;

    /// <summary>
    /// Скорость, ниже которой поток не идёт вовсе. Заполняется только для пыли хода —
    /// см. <see cref="MovementParticles.MinSpeed"/>.
    /// </summary>
    public float MinSpeed = 25f;

    /// <summary>Скорость, при которой поток идёт в полную силу.</summary>
    public float FullSpeed = 120f;

    /// <summary>Сколько секунд после остановки поток ещё считается работающим.</summary>
    public float StopDelay = 0.5f;

    /// <summary>Есть ли в эффекте хоть что-то, что стоит проигрывать.</summary>
    public bool Empty => Parts.Length == 0 && Stamps.Length == 0;
}

/// <summary>
/// Запечённое объявление отметины копоти — то, что в сцене эффекта задаётся узлом
/// <see cref="ScorchStamp"/>.
/// </summary>
public sealed class StampProfile
{
    public ScorchDecal Decal;
    public Vector2 Offset;
    public float MinSize;
    public float MaxSize;
}
