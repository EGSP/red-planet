# Friday Facts #204 - Another day, another optimisation

- Источник: https://factorio.com/blog/post/fff-204
- Авторы: kovarex, Zulan
- Дата: 2017-08-18
- Темы: производительность, работа с памятью
- Применимость к проекту: высокая
- Что извлечь: Предварительная загрузка данных в кэш перед обходом сущностей: скорость обновления определяется доступом к памяти, а не вычислениями. Приведены замеры на нескольких фабриках.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello,
as the team is getting slowly bigger and we still don't have any dedicated project manager, we had to start looking for tools to help us manage the team. We are testing software that allows our team members to track time spent on individual tasks, so right now my timer on "Friday facts related work" is running. I hope it to give me better insight into what kind of tasks our time goes to, where are we losing most of it, or what were the people doing when I was not here. People tend to not like these kind of changes, but we just have to admit that we are not the 4 people punk development team working from our living room and we need to invest more time into working efficiently.

### Prefetching (Technical) Zulan

Kovarex already presented a concise summary of the prefetching patch, here is some more background and dirty technical details.

I started to look into Factorio performance improvements a while back, more specifically UPS (updates per second) improvements for large bases. It is widely recognized that the UPS are mostly limited by memory performance ([more](https://www.reddit.com/r/factorio/comments/4h647g/factorio_performance_test_cpuram_based_fpsups/)). That is normal - even highly optimized scientific simulation codes are rarely limited by arithmetic instructions.

At first, I looked into ways to reduce the size of Entities. Common entity sizes like  Inserter (536 bytes) or AssemblingMachine (648 bytes) seem surprisingly large at first. I tried some changes, e.g. moving less frequently accessed data out of the actual entity in a separate object in memory. These changes had significant impact to the code in many files, but just saving a few bytes didn't make a measurable impact to performance.

Back to a bit of theory - there are two different ways in which memory can become a bottleneck: bandwidth (the amount of data supplied over time, e.g. 50 GB/s) and latency (the time until a requested piece of data is available, e.g. 50 ns). Comparing the results for different RAM timing settings (CAS latency) shows, that latency has a significant impact. It is important to note, that Factorio is not a homogeneous workload - some parts are still limited by memory bandwidth, others by CPU.

Modern CPUs are extremely good at mitigating memory bottlenecks by using caches, speculative execution and prefetchers. However, all active entities are read at every tick of the game. In large factories, this is too much data for caches. Also a virtual function call - such as the update of an entity - cannot be executed speculatively. Prefetchers are a part of the CPU that predicts what memory is going to be accessed soon and transfers it even before it is explicitly loaded. But since the entity update loop iterates over a linked list - the address of the next entity is stored within the entity itself - it is difficult to predict ([not impossible](https://compas.cs.stonybrook.edu/~nhonarmand/courses/sp15/cse502/slides/13-prefetch.pdf)).

This is where software prefetching comes in - the programmer gives a hint to the CPU what memory is accessed soon. That is what we now do in Factorio: Before an entity is updated, the next entity is already requested so that it can be loaded in the background. The principle also applies to a few other loops over linked lists. The nice thing about this, is that it is an extremely simple and isolated change in the code. The downside is, that you are entering the realm of architecture-specific micro-optimization. If you aren't careful, it can even be [bad for performance](https://lwn.net/Articles/444336/).

A good rule is to never guess about performance - always verify. So I did some tests with different maps and the results were promising. Entities are larger than a single cache line and the pointers point into the middle of the object due to multiple inheritance. Many experiments later, the optimal range showed to be -128 byte to +384 byte (8 cache lines). This coincides with the sizes of typical entities. The prefetching instruction has another parameter determining the cache level used - which again was determined experimentally.

![](https://cdn.factorio.com/assets/img/blog/fff-204-prefetch-comparison.png)

To get a bit more diversity, the measurements for this chart were done on a different CPU (i7-6700K vs i7-4790K previously), and include some more maps. It showed that the new belt-heavy map got less speedup (+5%) from software prefetching than the others. As a remedy, this map gets a huge boost from the belt optimization before. Other saves got a nice 9-13% speedup. All measurements are averages update times over 3600 ticks, the boxplots show 20 repeated runs.

Overall software prefetching is a nice effective micro-optimization with very little code changes, but many measurements to find the right configuration and verify.

### Crafting machine animation optimisation

The issue is, that crafting machines can have arbitrary count of secondary animations tied to it (rotating fan, liquid in the chemical plants etc.). As each of the animations can have different speed and frame count, we kept positions of all of these animations in dynamically allocated vector and just updated each of these independently whenever the crafting machine was producing. But now, we just have one number representing the overall offset of the animations. We move it depending on the speed of the crafting machine and all the animations calculate their cyclic position depending on the modulo of this value only when we need to actually draw the machine.

This means, that this complicated code:

```
void CraftingMachine::setupWorkingVisualisationFrames(double performance)
  {
    const CraftingMachinePrototype& prototype = *this->getPrototype();
    this->frame.move(performance, prototype.animation.getAnimation(this->direction));
    if (this->workingVisualisationFrames.empty())
    {
      this->workingVisualisationFrames.resize(prototype.workingVisualisations.size());
      for (size_t i = 0; i workingVisualisationFrames.size(); ++i)
        this->workingVisualisationFrames[i].randomize(prototype.workingVisualisations[i].getAnimation(this->direction),
                                                      this->getMap().getRandomGenerator());
    }
    for (size_t i = 0; i workingVisualisationFrames.size(); ++i)
      this->workingVisualisationFrames[i].move(prototype.workingVisualisations[i].getAnimation(this->direction));
   }
```

Becomes this simple:

```
void CraftingMachine::setupWorkingVisualisationFrames(double performance)
  {
    this->frameReference += performance;
    this->showWorkingVisualisations = true;
  }
```

The memory size of crafting machine is decreased and the overall performance of game is improved by additional 2%.

Another day, another optimisation :)

### HR Lab

The weekly dose of update high resolution graphics:

![](https://cdn.factorio.com/assets/img/blog/fff-204-laboratory.gif)

Related to HR entities, It turned out that our zooming system never showed an exact zoom of 2.0, which would be the 'pixel perfect' zoom level for the HR entities. By changing the zoom rate from 1.1, to the 7th root of 2 (1.104089...), the zoom now increments perfectly from 1.0 to 2.0 in 7 steps.

As always, let us know any thoughts or feedback over on our [forum](https://forums.factorio.com/51972).
