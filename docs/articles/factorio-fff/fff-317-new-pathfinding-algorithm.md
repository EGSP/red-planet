# Friday Facts #317 - New pathfinding algorithm

- Источник: https://factorio.com/blog/post/fff-317
- Авторы: Oxyd, TOGoS, Klonan
- Дата: 2019-10-18
- Темы: поиск пути, производительность
- Применимость к проекту: высокая
- Что извлечь: Иерархический поиск пути: карта разбита на участки, между ними ищется грубый маршрут, внутри — точный алгоритмом A*. Основная запись серии по поиску пути.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## New pathfinding algorithm Oxyd

Last week we mentioned the change to make biters not collide with each other,
but that wasn’t the only biter-related update we released this past
week. Somewhat coincidentally, this week’s updates have included something I’d
been working on for a few weeks before – an upgrade to the enemy pathfinding
system.

### Pathfinding

When a unit wants to go somewhere, it first needs to figure out how to get
there. That could be as simple as going straight to its goal, but there can be
obstacles – such as cliffs, trees, spawners, player entities – in the way. To
do that, it will tell the *pathfinder* its current position and the goal
position, and the pathfinder will – possibly after many ticks – reply with a
*path*, which is simply a series of waypoints for the unit to follow in order to
get to its destination.

To do its job, the pathfinder uses an algorithm called A* (pronounced 'A
star'). A simple example of A* pathfinding is shown in the video below: A biter
wants to go around some cliffs. The pathfinder starts exploring the map around
the biter (shown as white dots). First it tries to go straight toward the goal,
but as soon as it reaches the cliffs, it 'spills' both ways, trying to find a
position from which it can again go toward the goal.

[видео: fff-317-basic-pf.mp4](https://cdn.factorio.com/assets/img/blog/fff-317-basic-pf.mp4)
[видео: fff-317-basic-pf.webm](https://cdn.factorio.com/assets/img/blog/fff-317-basic-pf.webm)

*In this video, the algorithm is slowed down to better
show how it works.*

Each dot in the animation represents a *node*. Each node remembers its distance
from the start of the search, and an estimate of the distance from that node
toward the goal – this estimate is provided by what's called a *heuristic
function*. Heuristic functions are what make A* work – it's what steers the
algorithm in the correct direction.

A simple choice for this function is simply the straight-line distance from the
node to the goal position – this is what we have been using in Factorio since
forever, and it’s what makes the algorithm initially go straight. It’s not the
only choice, however – if the heuristic function knew about some of the
obstacles, it could steer the algorithm around them, which would result in a
faster search, since it wouldn’t have to explore extra nodes. Obviously, the
smarter the heuristic, the more difficult it is to implement.

The simple straight-line heuristic function is fine for pathfinding over
relatively short distances. This was okay in past versions of Factorio – about
the only long distance pathfinding was done by biters made angry by pollution,
and that doesn’t happen very often, relatively speaking. These days, however, we
have artillery. Artillery can easily shoot – and aggro – massive numbers of
biters on the far end of a large lake, who will then all try to pathfind around
the lake. The video below shows what it looks like when the simple A* algorithm
we've been using until now tries to go around a lake.

[видео: fff-317-long-pf-before.mp4](https://cdn.factorio.com/assets/img/blog/fff-317-long-pf-before.mp4)
[видео: fff-317-long-pf-before.webm](https://cdn.factorio.com/assets/img/blog/fff-317-long-pf-before.webm)

*This video shows how fast the algorithm works in
reality; it hasn’t been slowed down.*

### Contracting Chunks

Pathfinding is an old problem, and so there are many techniques for improving
pathfinding. Some of these techniques fall into the category of *hierarchical
pathfinding* – where the map is first simplified, a path is found in the
simplified map, and this is then used to help find the real path. Again, there
are several techniques for how exactly to do this, but all of them require a
simplification of the search space.

So how can we simplify a Factorio world? Our maps are randomly generated, and
also constantly changing – placing and removing entities (such as assemblers,
walls or turrets) is probably the most core mechanic of the entire game. We
don’t want to recalculate the simplification each time an entity is added or
removed. At the same time, resimplifying the map from scratch every time we want
to find a path could quite easily negate any performance gains made.

It was one of our source access people, Allaizn, who came up with the idea that
I ended up implementing. In retrospect, the idea is obvious.

The game is based around 32x32 chunks of tiles. The simplification process will
replace each chunk with one or more *abstract nodes*. Since our goal is to
improve pathfinding around lakes, we can ignore all entities and consider the
tiles only – water is impassable, land is passable. We split each chunk into
separate *components* – a component is an area of tiles where a unit can go from
any tile within the component to any other within the same component. The image
below shows a chunk split into two distinct components, red and green. Each of
these components will become a single abstract node – basically, the entire
chunk is reduced into two 'points'.

![](https://cdn.factorio.com/assets/img/blog/fff-317-components.png)

Allaizn’s key insight was that we don’t need to store the component for every
tile on the map – it is enough to remember the components for the tiles on the
perimeter of each chunk. This is because what we really care about is what other
components (in neighbouring chunks) each component is connected to – that can
only depend on the tiles that are on the very edge of the chunk.

### Hierarchical Pathfinding

We have figured out how to simplify the map, so how do we use that for finding
paths? As I said earlier, there are multiple techniques for hierarchical
pathfinding. The most straightforward idea would be to simply find a path using
abstract nodes from start to goal – that is, the path would be a list of chunk
components that we have to visit – and then use a series plain old A* searches
to figure out how exactly to go from one chunk's component to another.

The problem here is that we simplified the map a bit too much: What if it isn’t
possible to go from one chunk to another because of some entities (such as
cliffs) blocking the path? When contracting chunks we ignore all entities, so we
merely know that the tiles between the chunks are somehow connected, but know
nothing about whether it actually is possible to go from one to the other or
not.

The solution is to use the simplification merely as a 'suggestion' for the real
search. Specifically, we will use it to provide a smart version of the heuristic
function for the search.

So what we end up with is this: We have two pathfinders, called the *base
pathfinder*, which finds the actual path, and the *abstract pathfinder*, which
provides the heuristic function for the base pathfinder. Whenever the base
pathfinder creates a new base node, it calls the abstract pathfinder to get the
estimate on the distance to the goal. The abstract pathfinder works backwards – it
starts at the goal of the search, and works its way toward the start, jumping
from one chunk’s component to another. Once the abstract search finds the chunk
and the component in which the new base node is created, it uses the distance
from the start of the abstract search (which, again, is the goal position of the
overall search) to calculate the estimated distance from the new base node to
the overall goal.

Running an entire pathfinder for every single base node would, however, be
anything but fast, even if the abstract pathfinder leaps from one chunk to the
next. Instead we use what’s called Reverse Resumable A*. Reverse means simply it
goes from goal to start, as I already said. Resumable means that after it finds
the chunk the base pathfinder is interested in, we keep all its nodes in
memory. The next time the base pathfinder creates a new node and needs to know
its distance estimate, we simply look at the abstract nodes we kept from the
previous search, with a good chance the required abstract node will still be
there (after all, one abstract node covers a large part of a chunk, often the
entire chunk).

Even if the base pathfinder creates a node that is in a chunk not covered by any
abstract node, we don’t need to do an entire abstract search all over again. A
nice property of the A* algorithm is that even after it 'finishes' and finds a
path, it can keep going, exploring nodes around the nodes it already
explored. And that's exactly what we do if we need a distance estimate for a
base node located in a chunk not yet covered by the abstract search: We resume
the abstract search from the nodes we kept in memory, until it expands to the node
that we need.

The video below shows the new pathfinding system in action. Blue circles are the
abstract nodes; white dots are the base search. The pathfinder was slowed down
significantly to make this video, to show how it works. At normal speed, the
entire search takes only a few ticks. Notice how the base search, which is still
using the same old algorithm we've always used, just 'knows' where to go around
the lake, as if by magic.

[видео: fff-317-long-pf-after.mp4](https://cdn.factorio.com/assets/img/blog/fff-317-long-pf-after.mp4)
[видео: fff-317-long-pf-after.webm](https://cdn.factorio.com/assets/img/blog/fff-317-long-pf-after.webm)

Since the abstract pathfinder is only used to provide the heuristic distance
estimates, the base search can quite easily digress from the path suggested by
the abstract search. This means that even though our chunk contraction scheme
ignores all entities, the base pathfinder can still go around them with little
trouble. Ignoring entities in the map simplification process means we don't have
to redo it every time an entity is placed or removed, and only need to cover
tiles being changed (such as with landfill) – which happens way less often than
entity placements.

It also means that we are still essentially using the same old pathfinder we've
been using for years now, only with an upgraded heuristic function. That means
this change shouldn’t affect too many things, other than the speed of the
search.

## Bye bye TOGoS TOGoS

Hi everybody!
I'm going to be resigning from official Factorio staff after today
to start a new job closer to home.

The main reason for this switch is that working entirely remotely
turned out to be something that I wasn't able to handle all that well.
But also, my primary contribution to the project, the programmable map generator,
is complete, stable, and [pretty well understood by at least one other person](https://forums.factorio.com/74615#p451620),
so timing-wise this seemed a good point for me to move on.
To working in a cube farm writing Android applications for treadmills, apparently.

We'll see how that goes!

It's been an honor to be part of this awesome project
and to leave my imprint on it.
And working on a large and pretty well-managed C++ codebase,
full of things done just a bit different than I had ever thought to,
has been mind-opening.

I also want to thank the community for your continual patience, honest feedback,
and the occasional bit of pressure that you put on us to
make the game as good as it can be.

I'll continue to read the Friday Facts every week, lurk on the forums,
and probably update documentation here and there.
Hopefully I'll find time to crank out some terrain-altering mods of my own.
And I'm looking forward to seeing what else y'all do with it, too.
(Which includes working through a backlog of mods -- I have yet to
get to the space exploration part of Space Exploration!)

Peace out for now.

## Community spotlight - 13x9 Micro Factory Klonan

Over 100 Friday Facts ago we covered DaveMcW's 9x14 Micro Factory ([FFF-197](https://www.factorio.com/blog/post/fff-197)). While it may have taken over 2 years, he has improved his design even further, and the result is just as impressive as before.

He offers some more explanation of his Micro Factory in this [forum post](https://forums.factorio.com/76775).

As always, let us know what you think on our [forum](https://forums.factorio.com/76932).

                [Discuss on our forums](https://forums.factorio.com/76932)

                [Discuss on Reddit](https://www.reddit.com/r/factorio/comments/djlvsi/friday_facts_317_new_pathfinding_algorithm/)
