# Friday Facts #260 - New fluid system

- Источник: https://factorio.com/blog/post/fff-260
- Авторы: Dominik
- Дата: 2018-09-14
- Темы: симуляция, производительность, прототипирование
- Применимость к проекту: высокая
- Что извлечь: Модель течения жидкости: отдельный симулятор вне игры для проверки вариантов модели до её реализации.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hi Factorians,
This is Dominik, and my first FFF post ever! I will use this opportunity to talk to you about the exciting subject of pipes. Yeah, I know, right?

Spring came and with it Twinsen, saying "Pipes suck. Two people already tried to fix it and failed, who wants to be next?", and I’m like "Hey, that’s just pipes, you just make a simple simulation, simple AF. I’m in."

The conditions were even quite lenient:

Fluids get where they should.
They should act in a predictable manner, with reasonable splitting/joining in junctions.
Fluids can travel instantly, if need be.
Respect the pipe throughput limitations.
Flow can be viewed on the pipes.
Don’t do f**** up stuff like running in a circle indefinitely, sloshing back and forth endlessly etc.
Should be faster/more UPS efficient.

I am mostly working on the new GUIs, but still, the fact that summer is over and pipes are not done, kinda shows how simple fixing them is. Very naive I was.

## Problems with the current system

There are couple mishaps in the current pipe system. Good thing is that they work - the fluids do get from A to B, in most cases anyway. It follows a simple elevation model, fluidboxes will try to equalise with their neighbours, which is quite fast to evaluate and solves the simple cases, but it does not address much outside of running through a straight pipe.

![](https://cdn.factorio.com/assets/img/blog/fff-260-fluid-system.gif)

(Green column represents volume, Blue represents flow)

 The first of three main issues is that in junctions it behaves in a very random fashion. As a result, you might get recipients that are not getting any fluid where they obviously should be.

![](https://cdn.factorio.com/assets/img/blog/fff-260-fluid-system-bad-split.gif)

The second issue players voice is the limited behavior of the elevation functions. Only a fraction of the fluid is moved to its neighbour, so you rarely have a tank that is entirely full or entirely empty, which is a problem when you try to mix that with the integer based circuit network.

The third issue is performance. Even though the formulas are simple, they are calculated for every connection in every fluidbox, which adds up. As a result, nuclear power is unfeasible for megabases. There are other problems too, such as throughput loss over distance, fluids moving faster or slower depending on the entity update order, etc.

![](https://cdn.factorio.com/assets/img/blog/fff-260-fluid-system-bad.gif)

(Fluid flows much quicker in the bottom pipe)

## My simulator

The game is not at all practical for testing and debugging pipes, so I built a simple command line pipe simulator to develop the new system in. As I was attacking what I thought to be a simple problem, I started simple, and then had to refactor it several times whenever I realized that the problem is harder than what my simulator can support. I will shortly describe how it works, before going to the model itself.

The pipe layouts are loaded from a text file such as “5 1 \n s p p d 0”, which is 2 dimensions of the system, followed by a 2d layout, in this case just one row source-pipe-pipe-drain-empty. The simulator loads the layout, connects pipes and then updates the system tick by tick on request. I get a very rough overview, as picture below, but most of the time I have to inspect the running code to see what is going on under the hood.

![](https://cdn.factorio.com/assets/img/blog/fff-260-fluid-simulator.png)

Though now with the new map editor and the ability to step through single ticks, it will be much easier to test in-game too.

## Possible solutions

Before going to the current, simulation based model, I will shortly discuss other approaches that we have rejected.

### Optimizing the current system

This is something Harkonnen tried a long time ago, to reduce indirection in the update of the fluidboxes. Essentially, instead of each entity updating their own fluidbox, all fluidboxes in a segment would be kept in a singular part of memory, and then the simulation could be updated much faster. Initial experiments showed a performance increase of 30-50% updating all fluidboxes. However this would not address any of the other issues, and would add a significant amount of complexity to the currently quite simple handling of the fluidboxes, which we decided isn't worth the price.

### Flow model

Since we are doing flow here, flow algorithms look like a candidate. The most naive [Ford–Fulkerson method](https://en.wikipedia.org/wiki/Ford%E2%80%93Fulkerson_algorithm), although theoretically infinite, could work super fast in our case. Problem is that it only finds the maximum flow - the top limit of what we can push through. We could then divide this max fluid flow between the consumers and kinda get a working results. But the behaviour on the way would be ridiculous with full flow through one pipe and 0 through next, 0 to dead ends etc. In other words, the junction behavior would be entirely broken. Better balanced flow algorithms exist but these also don’t do exactly what we need and the complexity quickly jumps to astronomical realms.

### Electric network model

Another proposal was modelling like an electric network. Fluid flow is a popular analogy to their workings, and they do have a lot in common. The great thing about them is that they precisely model the flow branching and it could work out of the box. What it does not allow though is to limit the flow - one wire can, theoretically, run any current, but not so for the pipes, and we don’t want one pipe to be enough to supply whole factory. The limitations could be added, but that would, again, kill it with complexity.

A simplified version of this is what we consider the 'nuclear option'. In short, fluid network and pipes would work like the current electric network, instant transmission from production to consumption. This would increase performance by orders of magnitude, and remove the unintuitive flow of the current system, all consumers would get an equal split of the production, and storage tanks would act like accumulators.

However we have decided not to pursue this, for a few reasons.

There would be no visualisation or indication of the flow of fluid.
There would be unlimited throughput, one water pipe could supply all boiler and reactor setups.
It abstracts away part of the realism and charm of the game. (While this is subjective at best, it does mean something to us.)

### Merging fluidboxes

This would mean merging all the similar pipes in a segment into only a single fluidbox that needs updating. This would solve the performance issues, and the throughput loss over distance, however on its own, it would not solve the issues with update order, unintuitive flow direction/splitting etc.

### Physics

When it comes to the basic physics model, I ended up with something that is not at all realistic, but works well in practice. At the beginning, I tried to do almost realistic physics - as a proof of concept, with momentums and all that. But quickly I became cluttered with complicated formulas and it did not work at all. The system is so heavily discrete that physics are not really applicable.

In Factorio, a full content of a ~meter long pipe moves into the next pipe in one tick and instead of mixing single molecules in a junction all in real time (infinitely little steps) we have to mix/split huge blocks in one shot. It’s more like moving Lego blocks than running fluids. For a long time I was playing with pressure but I dropped even that in favour of just two variables - volume and speed, where the speed kinda models the pressure as well. Volume is the amount of fluid in the segment, speed affects how much of it wants to move on in a tick - as speed x volume. Just this is enough to provide a pretty decent simulation.

### Junction model

Most difficulties come from modelling the junctions. The general problem is that when anything does not behave entirely correctly, there exists a situation where it creates a total breakdown, such as no flow into some pipe.

There are colliding forces in play. We can only evaluate one pipe connection (one inlet/outlet) at a time, but the behavior needs to be symmetrical and fair towards pipes that are to be evaluated later (currently it is not). What is the right order of evaluation and how to make it symmetrical without super complex look-ahead?

Well, another big consideration is accuracy vs. complexity.

Over many iterations I kept developing models, followed by counterexamples that killed them.

## My model and improvements

The evaluation model I have arrived to works with two passes through the pipes in one tick. The formulas are more complex than the current one so it should be slower, but there will be one improvement to counterbalance it. The rough algorithm in one tick is as follows (I omit many necessary but boring details):

(Done at the end of previous step) Every pipe states how much fluid it intends to send to neighbours (called flow reservation) using a simple heuristic.
Perform topological sorting by direction of reservations from 0.
Evaluate pipes in the opposite direction, i.e. from end to start, against the expected flow.
Update a pipe in two stages:

Move fluid in it to neighbours proportionally to the space in them, space allocation they gave to the current pipe (providing they were already evaluated), and previous tick flow.
Allocate the resulting free space to neighbours that are yet to be evaluated to ensure they get fair share of it.

Go through the pipes again and do clean-up and calculate reservations for the next tick.

So in this algorithm, we go from the last pipe, always moving fluid and making space for the next one. The reservations/allocations system ensures good behavior in the junctions. Essentially at every junction, the fluid will try to be split evenly between all the possible connections, which makes things very intuitive.

It works nicely, but unfortunately this system is even more computationally demanding. Here comes in the key improvement which is taking every straight pipe (every segment that has max two connections) and connect it into one piece. No matter how long it is, it will be evaluated in one calculation. Naturally, this loses some realism, but it is insignificant as it is the junctions that matter and those will remain unaffected. So as long as you don’t do crazy stuff like making pipe grids and keep your pipes straight, they will have zero effect on UPS.

To put it more simply, updates will only be run on junctions and segments. At each junction, fluid will be split evenly among the neighbouring segments and junctions, any excess from one neighbour will spill evenly to the others. A segment is just a section of pipe without any splits, and fluid will transfer instantly through segments, but with a limited throughput.

![](https://cdn.factorio.com/assets/img/blog/fff-260-new-fluid-system.png)

So in the setup above, we would go from updating 32 individual pipes with the 'old' system, to updating 3 junctions (blue) and 8 segments. Since a segment can be any length, overall we expect a big performance increase. It could also lead to more UPS efficient designs, trying to minimize the number of splits in your pipe network, we know some players really enjoy trying to optimize for different metrics.

A last convenience improvement are some rounding mechanisms to fill or move away those 0.0001 fluids, draining the last drops from the pipe system if the sources are disconnected, and filling all the way up if production is greater than consumption. Another point left to consider is how to solve accidentally connecting pipes and tainting your whole oil system with water.

## Current state and next steps

All this is nice and already working, but still in the stage of the simulator.
What I still need to do to get it to 0.17:

Figure out good model for storage tanks, and how they fit in.
Perhaps refactor it from floats to integers, which would make it work more cleanly, but would also make some calculations more complicated. TBD.
Finish the algorithm for correctly drawing the flow direction in the connected straight pipes (not that simple).
Move it all into the game code.
Write tests for it (I am probably overreacting but I feel that this will take as long as all the work up to this point).

So to sum up, we have had this on our minds for a long time now, and performance was not the only issue we have considered. The new system will hopefully address all the issues we mentioned at the start.

As always, let us know what you think on our [forum](https://forums.factorio.com/62489).

Actually, speaking of the forum, you might notice that it underwent some changes this week.  Sanqui has updated our forum from phpBB version 3.0 to 3.2, which is a bigger jump than it sounds. It brings us more in line modern web features, the forums are now usable on phones, it heightens security, and paves the way for future extensions. Some style changes are final, but if you have any particular gripes, please say so and we will try to accommodate everybody.
