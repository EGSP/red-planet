# Friday Facts #331 - 0.18.0 release & Train pathfinder changes

- Источник: https://factorio.com/blog/post/fff-331
- Авторы: Klonan, V453000, boskid
- Дата: 2020-01-24
- Темы: поиск пути, симуляция
- Применимость к проекту: высокая
- Что извлечь: Разбор четырёх дефектов функции стоимости в поиске пути поездов: каждая ошибка в штрафах приводила к выбору заведомо худшего маршрута.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## 0.18.0 release Klonan

Early this week we pushed the deploy button on 0.18.0 ([patch notes](https://forums.factorio.com/80247)). This was quite a surprise to many of our players, as more typically the time between major releases and the scope of the release is greater. However this isn't like the old days, we are trying to keep the size of releases as small as possible ([FFF-314](https://www.factorio.com/blog/post/fff-314)).

What this means, is that what is currently in 0.18 is only really a small part of the work needed to be done on 0.18, and releases in the coming months will continue finishing off our 0.18 task list.

**Once everything on the 0.18 list is completed and the time is right, we will turn 0.18 into 1.0.**

What we have accomplished with 0.18.0:

GUI

Main menu redesign

GFX

Water animation
Tree animation
Color correction (LUTs)
New explosions and damage effects

Other

Optimizations
New Particle system
First work on new sound design
Steam login

What we have left to do in 0.18:

GUI

Character GUI
Blueprint library
Statistics GUIs (production, electric network stats, etc.)
Entity GUIs (Inserter, Assembling machine, chests, etc.)
Main screen GUIs (Chat, minimap, etc.)
Many more...

GFX

Offshore pump redesign
Assembling machine redesign
Beacon redesign
High-res icons
Final tweaks and polish

Other

Further sound design improvements
Mini-tutorials
Replace NPE with old tutorial
Final game balancing and tweaks
Finalised locale and proofreading

With this in mind, it wouldn't make sense to mark 0.18 stable before most of the above is finished. We made 0.18 a major version because it will break mods with all the changes we are making, and while initially it hasn't broken that much, many things to come will have a bigger impact, such as the Character GUI.

### Character GUI?

Initially we planned for the Character GUI to be in the first 0.18 release. However the task was proving quite difficult the way it was written, and when the release date came it was not ready, so we delayed the entire 0.18.0 release (twice). After a tough and thorough review, we decided to discard most of the work, and start fresh with a different team member. With programming, you've got to know when to hold 'em, know when to fold 'em.

With this change, we decided to release 0.18 without the Character GUI, the alternative was an additional 4-6 week delay, which with only 8 months left, is a big chunk of time. We also don't want to get back into the old habit of always delaying the release for another and another reason, and the things we present in Friday Facts not being seen in-game for months and months.

After this decision, Dominik, who was working on the Character GUI, decided to leave the Factorio team.

## Campaign cancelled V453000

In the recent [FFF-329](https://factorio.com/blog/post/fff-329) we have mentioned two different approaches to the campaign. In this FFF, we are announcing that the campaign has been cancelled, and I’ll try to explain why.

After the Tutorial/NPE was cancelled, I realized several things and it made me re-evaluate what the campaign is trying to be, asking questions like:

Is the campaign meant to be 'The Way' how to play the game or just 'extra content'?
Should the campaign try to mimic Freeplay?
Is losing progress and starting from scratch that much of a problem?
...

In my mind, if we would be trying to create 'The Way' how Factorio is intended to be played, it would make sense to try to stay very close to how Freeplay works, since that has been 'The Way' for many years now. However trying to mimic Freeplay inherently means it's never going to be identical to Freeplay, while certainly adding some new problems (and hopefully benefits).

Trying to be similar to Freeplay and create a representation of how Factorio 'should be played' was the mindset behind the work-in-progress expanding campaign. The main benefit compared to Freeplay would be that the content is segmented into smaller chunks (which is already kind of the case due to the science pack production/technology progression - the player can't build everything from the start). However, having a short-term quest, being in a limited area of the map, interacting with existing factory ruins that prevent the player from going to the next area, etc. brings a lot of unexpected problems. It was just not fun to play in comparison.

With that, the Freeplay with all its freedom will probably always be the best way how Factorio is experienced. It would make a lot of sense (to me) to rather strive to create a set of smaller scenarios as side content to give a fresh experience after 2,000 hours of Freeplay. To extend Factorio with separate scenarios, instead of trying to re-invent Factorio with the expanding campaign.

Being side content, I believe it'd be much more acceptable if progress is lost between levels if it creates fresh gameplay or an interesting challenge. The reason we chose the expanding map concept was to never force the player to lose progress and rebuild their base. If this restriction is lifted, we could split the scenarios over many levels. Over the Christmas break, I tried to recycle the work-in-progress expanding map into a set of separate levels, and we even implemented a playable but very crude version of them in the first week of January.

We sat down last week to reconsider what to do with the campaign out of the two options from [FFF-329](https://factorio.com/blog/post/fff-329). The expanding campaign felt too risky due to all its problems, and we were not sure enough about it. The separate levels would be a safer option, but if they are not the main way to play the game, then they are not as important as the core of the game for the 1.0 release.

It is still possible we will add a campaign after the 1.0 launch, but for now we believe that abandoning the idea of a campaign gives us the focus we will need on the core of the game for the upcoming months.

## Trains Pathfinder changes boskid

Today I would like to talk about changes in the Train Pathfinder I made for 0.18. They are subtle changes to fix weird corner cases, but first a short glossary:

Rail - a single entity over which trains can ride. The basic unit of building.
Segment - a series of rails that are not split in between by junctions, signals or stations. The basic unit for pathfinder.
Block - a group of segments that are colliding or connected without signals in between. The basic unit for train reservations.

in 0.17 and prior, the pathfinder used classic [Dijkstra](https://en.wikipedia.org/wiki/Dijkstra%27s_algorithm) to find the path with the smallest penalty. The nodes are related to segments with extra information about travel direction. Edges are related to segment connections (transitions).

### Issue 1: the last segment distance is not counted into penalty

This was the issue that made me look into the pathfinder code. Train stops are always at the end of a segment. Segments can have at most 2 train stops, one for each direction of travel. Any more is not possible, since the train stop inside would split the segment into 2. This means that a train entering the last segment on its path, must travel the whole distance of it to reach its destination.

As it was implemented, the total cost on nodes was computed as cost of all previous segments and transitions. That means the node with the requested train stop would be picked from a priority queue before the cost of the last segment distance was added. Since the node has the requested train stop on its end, the path would be returned even if there are other nodes with a higher current cost but would be better since the cost of the last segment would be lower. The cost of the current segment would be added to the new nodes during expansion but it was already too late - the path was returned.

![](https://cdn.factorio.com/assets/img/blog/fff-331-1-old.png)

*0.17 - Pathfinder chooses the bottom path which is longer*

The fix was quite simple: include the node's segment length into the node's total cost from the start. This changed the node expansion code as it was not adding the cost of the current node's segment, but the next node's segment. This would lead to cases where the first segment distance would not be counted, so I added code that when creating starting nodes, creates them with the penalty of the whole starting segment.

![](https://cdn.factorio.com/assets/img/blog/fff-331-1-new.png)

*
0.18 - Fixed Pathfinder chooses upper path that is shorter
*

### Issue 2: the opposite station in the last segment is not counted into the penalty

The second issue is also related to the last segment and has a similar explanation as the issue above. The cost of the train stop in the current segment was added only when expanding node to the next segments.

![](https://cdn.factorio.com/assets/img/blog/fff-331-2-old.png)

*
0.17 - The pathfinder chooses the bottom path because it does not contain the penalty of the opposite station
*

The simple fix of shifting the train stop penalty by 1 segment (when expanding, add the cost of the train stops to the next segment, not the current segment) and including both train stops inside the start segment fixed the issue (this is not the final solution as it will be explained in issue 5).

![](https://cdn.factorio.com/assets/img/blog/fff-331-2-new.png)

*
0.18 - The fixed pathfinder chooses the upper path that is shorter. Both paths have a penalty of 1 station.
*

### Issue 3: the first segment distance would not consider train position

Now after the fix for issue 1 I noticed that adding the penalty of the whole starting segment has a flaw - if a train can go in both directions and both train ends are in the same segment, the cost of the first segment would be same in both directions and would cancel out.

![](https://cdn.factorio.com/assets/img/blog/fff-331-3-old.png)

*
0.17 - The pathfinder chooses to go left. Path to the right has a cost higher by 2.
*

The fix here was simple: since the pathfinder is only given the start rail in the first segment, it has to go by all the rails in a given direction to find the end rail in a given segment. I have added the distance measurement during that search and used it as the initial cost. That way the position of train will affect the initial cost when looking at paths for both directions.

![](https://cdn.factorio.com/assets/img/blog/fff-331-3-new.png)

*
0.18 - the fixed pathfinder chooses to go to the right. The train position is considered.
*

### Issue 4: a single segment path had priority over multi-segment paths

Now that issue 3 was fixed, there happened to be extra code to handle single segment paths. It covers weird cases such as cyclic segment, or station in the same segment. This code is computing the path cost based on rails (not segments) and if it finds any path, it will choose the path of lowest cost and skip the main pathfinder logic for multi-segment paths.

![](https://cdn.factorio.com/assets/img/blog/fff-331-4-old.png)

*
0.17 - The pathfinder chooses the single segment path.
*

The solution for this - if there exists a single segment path, create marker node with cost of shortest single segment path and throw it into pathfinder priority queue. If this single segment path is truly the shortest, then this marker node will be picked up during expansion meaning all the multi-segment paths have a higher cost. If there would be a shorter multi-segment path, this marker node will not be picked up in time and so will be ignored.

![](https://cdn.factorio.com/assets/img/blog/fff-331-4-new.png)

*
0.18 - The fixed pathfinder chooses the multi-segment path because it has a lower cost.
*

### Issue 5: the opposite station in the first segment was added into the penalty

After the fix for issue 2, every segment on the path with train stops would add the penalty of the train stops, not counting if it was on an entrance or exit of the segment. This means that a double-sided train for which one end is inside the segment with a train stop that is under the train, would still consider that train stop in the penalty.

![](https://cdn.factorio.com/assets/img/blog/fff-331-5-old.png)

*
0.17 - The pathfinder chooses the left path since the right path has the penalty of the train stop
*

Based on this, I decided to discard the penalty of opposite train stops in the first segment. Doing performance measurements here showed another issue: the fix for issue 2 was flawed. The penalty of the goal train stop in the last segment should not be added, because this forced the pathfinder to over-expand nodes up to the train stop cost looking for other paths that maybe would not have that last station penalty (when in fact, it was unavoidable). So now I saw: the opposite train stop in the first segment and the goal train stop in the last segment must not add a penalty. This gave the final solution for the train stop penalty: when expanding a node, the exit train stop in the current segment adds a penalty and opposite train stop in the next segment entrance adds a penalty.

![](https://cdn.factorio.com/assets/img/blog/fff-331-5-new.png)

*
0.18 - The fixed pathfinder chooses the right path. Train stop under the train is not counted.
*

### Issue 6: the block distance from the start was not updated properly

This is quite subtle issue. It is related to this penalty rule:

*"When the rail block is occupied by a train -> Add a penalty of 2 * length of the block divided by block distance from the start, so the far away occupied paths don't matter much."*

This means that looking for path, not only the cost from the start is counted, but also the number of blocks from the start - every time a node expands to another segment that is from a different block than the previous node's segment, increase the total block count from start by 1. That was working fine as long node expansion would create a new node. In the case of updating a node, the total block count was not updated leaving the old value in the node. That means the updated node would have the cost of new, lower cost path, but would have the block distance from the start of the previous path.

![](https://cdn.factorio.com/assets/img/blog/fff-331-6-old.png)

*
0.17 - The pathfinder chooses the bottom path because blockDistanceFromStart is not properly updated in the upper contraption.
*

In this contraption, the issue happens in the upper part. The segment under the upper middle locomotive is expanded first when going from the straight segment on its left - it was of lower cost. Now this segment has the cost of the straight path and the block distance from start of 1, since there was only 1 transition to a different block. There was also the penalty of the block being occupied by another train.

When expansion goes through the upper siderail, it hits 4 rail signals and so the block distance from start is increased up to 4. This path has a higher cost up until the left rail signal near the upper middle locomotive. Here, however, the cost of the block occupied by another train is lower since we are entering the 5th block from start when going through the upper path. The side-rail path is chosen to update the node cost related to the segment with the upper middle locomotive, but the block distance from start was not updated. Because of this, the penalty on the next rail signal that goes to another occupied block, was equal to the last segment distance divided by 2 instead of 6. This difference would make the path through the bottom contraption of lower cost since the straight path here is cut and the node for the segment under the middle locomotive is created with the proper block distance from the start.

The fix was simple: when changing the cost from the start on an existing node, also change the block distance from start.

![](https://cdn.factorio.com/assets/img/blog/fff-331-6-new.png)

*
0.18 - Fixed pathfinder chooses upper path
*

### Issue 7: repathing would clear a train's counter of ticks waiting on signal.

This is not exactly a pathfinder issue but it lead to trains being stuck. If a train is waiting on a signal, it will repath periodicaly to find another possible path that may now be unblocked. The repath however clears the counter of how long the train is waiting on the signal. Periodic repath logic was aware of this so it would restore the previous waiting on signal tick counter when the train entered arriving-to-signal state. However some fix ago changed train behavior, and the train no longer goes through the arriving-to-signal state and so the tick counter was cleared. This broke the following rule:

*"When the path includes a train currently waiting at a rail signal -> Add a penalty of 100 + 0.1 for every tick the train has already waited."*

![](https://cdn.factorio.com/assets/img/blog/fff-331-7-old.png)

*
0.17 - The right train is stuck because the time-dependent penalty of the left train is reset every time the left train repaths.
*

Universal fix here: clear this counter only if the train changes state to anything other than waiting-on-signal.

![](https://cdn.factorio.com/assets/img/blog/fff-331-7-new.png)

*
0.18 - The fixed ticks waiting on signal logic, after 3 repaths the penalty of left train increases to a point where right train chooses the long path.
*

### Train Pathfinder optimisation - priority queue

In the heart of the pathfinder there is a priority queue that collects open nodes. As it was implemented up until now, the priority queue was based on a double-linked list. Finding the node with the lowest cost (highest priority) was fast (constant), but inserting new nodes or updating existing nodes would be, in worst case, linear (O(n)). After a quick prototyping phase, I decided to implement a binary heap with min-property over array. This change alone gave around a 20% speedup to the pathfinder.

### Train Pathfinder optimisation - heuristic (conversion from Dijkstra to A*)

The second optimisation applied was the addition of a heuristic function that gives the minimum cost to any end. This means that node expansion is now guided into the goal by the heuristic function, and so less nodes should be required to visit before finding the goal. This gave another performance increase.

The rough guess as to impact of the train pathfinder changes, is that its about 2x faster, and should have fewer weird edge-cases.

As always, lets us know what you think on our [forum](https://forums.factorio.com/80410).

                [Discuss on our forums](https://forums.factorio.com/80410)

                [Discuss on Reddit](https://www.reddit.com/r/factorio/comments/etb1ci/friday_facts_331_0180_release_train_pathfinder/)
