# Friday Facts #209 - Optimisation is a way of life

- Источник: https://factorio.com/blog/post/fff-209
- Авторы: kovarex
- Дата: 2017-09-21
- Темы: производительность, симуляция
- Применимость к проекту: высокая
- Что извлечь: Серия оптимизаций: электрическая сеть, дым, логистические боты; для каждой указаны приём и полученный выигрыш.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
I need to make a confession.

  I'm addicted.

  Addicted to optimisation.

## Optimisations for 0.16

  I earned some money from the game... you know... I could just... I don't know... play games and... have some leisure time, but do you know what would be the problem? I would have to think about some Factorio optimisations I wanted to do anyway.

  When I go to bed in the evening, I think about cache locality and data structures. I dream about ways to reorganize structures to minimize the amount of data needed to be read to perform Factorio logic. When I walk in the park, I visualize the tricks that could be done to decrease data and logic dependencies. This usually only stops when I see how many bugs need to be fixed after release when lot of changes are done.

### Electric network

  The electric network optimisation for 0.16 was simple in principle. We currently have one continuous buffer of electric connector data stored in the electric network. It needs to be iterated twice (first for calculation, second for power distribution).

![](https://cdn.factorio.com/assets/img/blog/fff-209-previous-electric-network-organisation.png)

  The change was to categorize energy connector data by the prototype:

![](https://cdn.factorio.com/assets/img/blog/fff-209-categorizing-electric-energy-sources.png)

  It adds another layer of indirection when something needs to be added/removed from the network, but that happens quite rarely. The advantage is, that all the prototype specific values, like *inputFlowLimit*, *outputFlowLimit* etc. can be accessed once at the beginning of the processing of one category, which (together with other size squishing), results in a much smaller memory layout, which results in faster iteration of the algorithm.

  The conclusion is that electric network transfers are more than twice as fast compared to 0.15.

### Smoke

  Smoke already had the first iteration of optimisation [125 weeks ago](https://www.factorio.com/blog/post/fff-84). But Factorio is many times faster since than, so smoke started to appear in our profiling again.

  The problem was that even creating the smoke object and registering it on the map was slowing the game down. The plan was to make something we internally call TrivialSmoke. The Trivial smoke data size is 32 bytes which is quite small compared to the previous "smoke as entity" size of 256 bytes. It contains only the most basic data squashed as much as possible, and it is stored in a continuous piece of memory on the chunk it was created.

![](https://cdn.factorio.com/assets/img/blog/fff-209-trivial-smoke-buffer.png)

  As the updates happen in regular intervals (120 ticks), it is always guaranteed, that the smokes to be updated this tick are at the beginning of the buffer, I don't have to check the rest. The same buffer is used for update and for render, so these objects don't need any other references to it.

  It is similar to the *Chunk update planner* mentioned in [FFF-161](https://www.factorio.com/blog/post/fff-161), just flattened.
  The result is, as expected, that the smoke creation/update/removal times were significantly reduced.

### The logistic bots dilemma

  The dilemma is, whether to use similar approach to the logistic bots as for the smoke. This would mean that logistic bots would be created/updated/removed very fast and their memory footprint would decrease a lot, but they wouldn't be regular entities any more. They couldn't be mined (which could be for the better actually), couldn't be attacked or damaged by anything (which again, would be welcomed by some players). I'm quite sure that it shouldn't apply for the construction robots, as they are more related to the fighting part of the game, but logistic robots aren't.

  The dilemma is, whether changing the game rules like this *just* for optimisation isn't going too far.

### Running total

  I benchmarked a huge save of Vaclav ([Imgur - 0.15 Rocket science base](https://imgur.com/a/qDDs1)), and it can already run 2.4 times faster in 0.16 compared to 0.15 so we met our "2 times faster release" quota, but I'm afraid that this won't stop the addiction.

## Stone path update

  Almost all of the terrain has been updated for 0.16, which includes the high-res update of it. We will certainly cover the terrain/trees/decals improvements in future FFF. Here I would like to present the new version of the stone path terrain made by Ernestas. It is inspired (as many things in Factorio) by the eastern style of pavements, and I'm starting to be a fan of his work.

![](https://cdn.factorio.com/assets/img/blog/fff-209-stone-path.png)

As always, let us know your thoughts, ideas and feedback on our [forum](https://forums.factorio.com/52875).
