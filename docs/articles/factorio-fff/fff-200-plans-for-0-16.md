# Friday Facts #200 - Plans for 0.16

- Источник: https://factorio.com/blog/post/fff-200
- Авторы: kovarex, TOGoS
- Дата: 2017-07-21
- Темы: генерация мира, инструменты
- Применимость к проекту: высокая
- Что извлечь: Программируемый шум: генерация карты описывается выражениями в данных, а не кодом. Отдельная утилита предпросмотра карты без запуска игры.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
![](https://cdn.factorio.com/assets/img/blog/fff-200-fireworks.gif)

  Hello,

  I can't believe that we have been able to produce a post every Friday for 200 weeks without missing a single one. To be honest I'm not sure if this isn't the right time to pause for a while, to avoid being this kind of show that gets worse and worse over time until it is so bad that you want to take your intestines and strangle yourself with them. But people in the office convinced me with arguments like "FFF is the only good thing we have", so we probably have to continue for a little longer.

## 0.16 plankovarex

  Our 0.16 trello card list is long, probably too long, it contains 80 cards. Many of them have a similar history as this one:

![](https://cdn.factorio.com/assets/img/blog/fff-200-typical-trello-card.png)

  The main reason of showing this is, that that it is almost certain, that some of these tasks will be pushed again to next version, or away completely. It depends how fast will the work on the more important things progress.

  The bigger things are:

- Artillery train

- GUI/UX improvements

- Mod portal improvement (rewrite)

- Map generation improvement (related to the updated high res terrains and decoratives)

  Then there are optimisations, we want to tackle mainly these:

- Train pathfinding and collision checking optimisations

- Additional smoke related optimisations

- Lamp related optimisations

- Item stack optimisation

- Optimisation of electric network update logic

- Optimisation of logistic robot movement

- Group multiple item icons to one sprite in order to optimize rendering of saturated belts

  There are overall tweaks planned like:

- Find a way to show connected walls + pipes in a blueprint

- Confirm blueprint deletion

- Fluid squashing in pipes

- Map interaction improvements

  And there are these semi-bugs or small things like:

- Train block its own path with chain signals

- Make programmable speaker 'Global playback' only apply for people on that force

- Electric network UI weird with lots of accumulators.

  Lets hope we can finish at least half of the stuff we plan for 0.16.

## Map generationTOGoS

The current terrain generation tends to make a world that looks the same everywhere.
With the goal of making exploration more rewarding we've been working on a branch called "mapgen-fixes",
which introduces some new biomes, new decoratives, and changes to the way things are distributed.
I've been tasked with fixing up the obviously screwy parts
(next on the to-do list is to deal with an overabundance of biters)
so that this can finally get merged to master and others can start testing and giving their feedback.

On the one hand this kind of work is fun because you have a bit of artistic freedom.
There is no 'right answer' to what the Factorio world should look like,
so I can play with settings and try to create a result that I think is interesting.
On the other hand, not having a 'right answer' means you can't just write a unit test to tell if your job is done.
I needed a way to look at results from the map generator
and compare changes across versions
that would be more efficient and easier to automate than loading up the entire game
and poking around in the map editor, waiting for chunks to load,
and then trying to remember what it looked like before and asking myself
if this is better or worse.  So I created a map preview generator.

### Map Preview Generator

In the interest of being able to compare against maps generated with the current versions of Factorio,
I got the map preview generator merged early.  In fact it's already available on 0.15;
you can use it from the command-line like so (adjust path to Factorio binary as needed):

bin/x64/factorio --generate-map-preview=output-name.png --map-gen-seed=1230 --map-preview-scale=4

![](https://cdn.factorio.com/assets/img/blog/fff-200-map-preview-1.png)

The above map is 900x900 pixels and has a resolution of 4 meters per pixel, so is 3.6km on a side.
To give some sense of scale, a medium-sized factory with mining outposts could easily extend to the edge,
but to cover the entire area would be rather impressive.

If you make a very zoomed-out map (scale=32 is the maximum supported for 0.15.x)
you may notice that it takes a long time to generate the preview.
On 0.16 this will have been taken care of by
refactoring the terrain generation system to operate at arbitrary scales
instead of always generating 32x32-tile chunks.
To understand why this works, you need to understand that map generation is done in 2 phases:

- Calculating the value of several variables (elevation, temperature, humidity, and a few others) for every point on the map. This is done by sampling a [coherent noise function](http://libnoise.sourceforge.net/glossary/#coherentnoise) (whose basis is similar but not identical to Perlin noise). The value at each point can be calculated solely based on the point's coordinates and the map seed, making this part 'embarassingly parallelizable' and easy to do at any scale (and with a bit of further refactoring, by arbitrary transformations of the inputs).

- One chunk at a time, 'correcting' the results of the first step, making sure entities don't overlap, replacing combinations of tiles that we don't have good transition graphics for, etc. This is where we enforce that the only thing that can be adjacent to water is grass, because that's the only transition we have graphics for (which happens to be something the art guys are working to rectify).

For the sake of making the map preview generator fast and simple,
only the first step is done.
This is a good enough approximation for my purposes,
though if you look closely you may notice that shorelines are off by a tile or two in some places.

### Programmable Noise

One of the things that I disliked about how Factorio's map generation works even before I was hired to improve it, was that
once you've expanded your base to a certain size,
exploration of the map ceases to be interesting.
At an extreme scale this uniformity is exceedingly obvious,
but it is also apparent at realistic factory scales.
There are no especially large lakes to work your way around,
and different parts of the map don't really have any unique character.

Take for instance this super zoomed-out map (32 tiles per pixel):

![](https://cdn.factorio.com/assets/img/blog/fff-200-map-preview-2.png)

In part, this is due to the underlying noise functions we use not being very flexible.
The function to calculate elevation, for example,
is defined by a very small number of variables, including seed,
number of [octaves](http://libnoise.sourceforge.net/glossary/#octave),
and [persistence](http://libnoise.sourceforge.net/glossary/#persistence).
Persistence itself is varied over the map so that in some areas you get smooth coastlines and in others very noisy ones.
This simple specification does a pretty good job of generating random-looking worlds,
but it doesn't give as much control as I would like in order to make different parts of the world look different.

In order to efficiently calculate terrain,
a lot of the low-level code is dedicated to caching noise values
and vectorizing calculations on them.
This is great, but means the high-level logic for how noise is generated
is rather buried in a lot of implementation details,
and is therefore difficult to reason about and even harder to change.
But abstractly, the noise function is just a functional program that adds and multiplies values from the basis function (at different scales) together.
In order to make this logic more obvious I've created an assembly-like language for specifying the steps to produce noise.
e.g. Take basis noise at a certain scale, divide it by persistence, add in basis noise at another scale, and so on.
This approach is super flexible, but the assembly language is not very nice to read or write.
A longer-term solution will be to allow building noise functions from Lua code,
which will have the side-effect of making the system hackable by modders.

Abstracting high-level aspects of terrain generation out of the C++ code opens up a lot of possibilities.
Some ideas:

- Larger scale features that are modulated by distance so that the starting area remains relatively flat and predictable, but you find seas and larger continents far away.

- 'Ridge' some large-scale noise to produce linear features like mountain ranges or rivers.

- Altering the input to some noise layers with the output of another noise function. This can warp landforms in a way that [looks a bit like the result of plate tectonics](https://cdn.factorio.com/assets/img/blog/fff-200-map-land.png).

Related to this, we are also working to make the world seem a bit less flat, but that is probably better left for another FFF.

  As always, leave us any feedback or comments over on our [forum](https://forums.factorio.com/51131)
