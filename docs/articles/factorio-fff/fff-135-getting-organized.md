# Friday Facts #135 - Getting Organized

- Источник: https://factorio.com/blog/post/fff-135
- Авторы: Tomas
- Дата: 2016-04-22
- Темы: процесс, команда
- Применимость к проекту: низкая
- Что извлечь: Рост команды требует явных договорённостей: прозрачность, направление вместо управления, измерение продуктивности.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello all,

originally I intended to dedicate most of the post to the technical aspect of our new Multiplayer User Authorization mechanism that I have been working on in my programming time. Then I thought, hmm it would be nice to start with some project management changes that we are looking into and experimenting with. Then I pretty much ended up making a full blog post about that =) It might give you an interesting insight into issues that we are dealing with. The Multiplayer User Authorization mechanism will definitely be described in one of the future FFFs.

### Slowly growing

As mentioned in the previous FFF there is a new team member here with us. Her name is Michaela (or Mishka) and one of her main responsibilities is to get us a bit more organised=)

At the moment we are 11 people in the office with 2 external collaborators. Our short/midterm goal is to grow to about 15 people in the office (we are [still looking](https://www.factorio.com/job/senio-game-developer) for good C++ programmers ideally from Europe!).

Our approach to "project management" so far has been very loose and organic. Somehow we feel that we have reached a boundary here and need to start working a little bit on our methods of working together, sharing the results of work, etc. This especially holds true with our (moderate=)) growing ambitions.

### Defining the areas

So we spend quite some time brainstorming and figuring out what we believe is working well, what could be improved and where we want the things to go. We came up with 3 basic themes / areas for getting organised. In short these areas are: Transparency and knowledge sharing, Direction vs. management, Productivity. Let's have a look at the descriptions for all of these.

The next step was internally clarifying three aspects for every area:

- What is the current situation and why we want to change it.

- What is our goal.

- What tools and processes can we use to achieve the goal.

We are very aware that changes like these can be sensitive. Especially when we start using words like project management, goals, processes, etc. So our main criteria for the whole set of changes is to not overdo things. This means make small changes step by step and stop when we have achieved our goals. Also we are aware that processes and tools are here to serve us and not the other way around so we will try to keep our minds open and abandon things that won't work for us.

### Area 1: Transparency and knowledge sharing

Till now, often a developer would work on a task for quite some time without too much interaction with others. Hence there was a lack of feedback on how he is doing, if he is stuck or not, if what he is working on might be related to what a different dev is doing, etc. So far our main tool of communication has been talking in the office / at lunch and Trello cards. Trello has worked really [well for us](https://www.factorio.com/blog/post/fff-85), but it is getting full of cards, generally messy and often there is little information for particular cards and so on.

Ideally we are after a situation when people have a general overview of what others are working on. We are still small enough to do this for the team as the whole (imho it becomes harder for teams of 20 or more people). This should also improve productivity (people can help each other in case they know how to solve an issue someone else is stuck with).

We started by having a short stand-up meeting twice a week (currently Monday and Thursday at 1 p.m.). The meeting (ideally) takes about 15 minutes, requires all the team members to be present and standing (to keep the pace going). We start by announcements if there are any  (this and this person is coming for probation, we have made this and this presentation, etc.). Afterwards we go in a round and everyone gives a brief status. This means: what he is working on, if he is doing ok or is stuck, if he needs to communicate with other people, etc. This takes (ideally) less than a minute for a person. In the near future we intend to have "minutes" (short transcript) from the meeting in the written form to inform (and gather feedback) from the members not present.
The main inspiration for stand-up meetings came from my pre-Factorio (so long ago :D) programming job in Amsterdam, where it really helped us as a team of about 12 developers.

Another potential change we are looking into here is become more verbose in our Trello cards. This would mean giving proper description to cards, links to forums or other related cards, using checkbox lists, etc.

### Area 2: Direction vs. management

So far the project direction and project management "roles" have been concentrated in the hands of the same people (kovarex, me and Albert). This makes sense for very small teams however it is also quite limiting. The basic issue here is that the direction is more like a vision (I imagine a person who knows where the ship should go) while management is more like an every day task making sure that the ship actually goes where the direction has been set.

So we want to split the direction of the project from its management. The three of us would still be giving the project direction however the part of the management would be given to Mishka and part to team members themselves. This should free up some of our time which we can dedicate to programming / art as well as allow us to be outside of the office more (summer is coming =)).

We imagine that this would be achieved partly by cleaning up the Trello a bit. We (product owners) will need to spend more dedicated time making sure that the cards, priorities and assigned devs / artists in the Trello are well defined. On the other hands this frees our hand because when a person is finished with a task he / she will know what to do next.

Another thing we intend to improve in this area (overlapping with the next area) is the code reviewing process. At the moment most of the code reviewing is done by Michal in a kind of random manner (he reads some commits but not all of them). The goal would be to have code reviewing across the team. We are still looking for a simple and efficient tool to achieve this (let us know if you have any recommendations).

### Area 3: Productivity

We are not after moving forward as fast as possible at every cost. Balance is more important in our eyes - moving forward while still enjoying the work. However recently there has been quite a lot of distractions around the office and we feel it is time to start looking into this.

Haha, a good example from the real life. Just before I was about to write the next paragraph, in the room next doors there were two people programming and one shooting nerf gun bullets around (consistently hitting monitor of his college who was coding =))). This can be a good fun now and then, things like these decrease productivity not only of people involved but pretty much of all the people in the vicinity (most of the office). Again it is about finding balance.

So we are after decreasing distractions in the office a bit. We have started using [Slack](http://slack.com) and encouraged people to use it to communicate to others when the face-to-face talking is not necessary. Besides that, it comes down to being mindful about whether my actions (i.e. sharing the latest news from the web by shouting them out in the room, having fun with colleague at his computer) are not disturbing others too much.

Another aspect would be the oblivious "fight against procrastination". However we don't feel this is such a big issue at the moment. Hopefully all of the people here have a strong internal motivation to work on the game and make it as good as possible which should take care of this on its own.

### Back to the game

Ok so let's have a look at some progress at actually making Factorio and not only trying to get real making it. For quite some time Michal (posila) has been playing with fire and flamethrower turrets. Vaclav has been doing the art for these. This is quite a big subject which deservers a blog post (or substantial part of it) of its own and it will come in the near future. But to give you a bit of a sneak peek into the process you can checkout the hi-res render of a model of the flamethrower turret below:

![](https://cdn.factorio.com/assets/img/blog/fff-135-flamethrower-turret.jpg)

Maybe more than usual, we are curious about what you think. If you have any comments or tips regarding the areas mentioned above please let us know on [our forums](https://forums.factorio.com/24089).
