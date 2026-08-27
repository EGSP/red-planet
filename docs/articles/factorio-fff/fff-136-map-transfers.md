# Friday Facts #136 - Map Transfers

- Источник: https://factorio.com/blog/post/fff-136
- Авторы: Cube
- Дата: 2016-04-29
- Темы: сеть, сериализация
- Применимость к проекту: низкая
- Что извлечь: Передача карты новому игроку поверх UDP: собственный контроль перегрузки, повторная отправка потерянных фрагментов.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello all,

This week's instance of FFF is brought to you by cube, your friendly neighbor clueless network programmer. This post will be more technical than usual, so let us know if you found it interesting.

### Things go on

The week has been spent mostly on pushing forward in multiple of ongoing areas in the development for 0.13. Robert is working on Combinators, Posila and Vaclav on Fire and Flamethrower Turret, Martin is immersed in Factorio mod portal and its integration and Tomas and Ondra have been tweaking some Multiplayer Matching server stuff. While Kovarex has spent most time on the 0.12 bugfixing. Albert is back home for a week so the full speed GFX work will happen from next week on.

### The Problem

Factorio has long had problems with map upload speed.
As usual the hard problems they always appear "out there" and never when you're looking for them,
so while we were getting a steady stream of bug reports about this problem, we
could never replicate it.

You might ask why do we even need to upload the whole map when the player might
end up seeing just a tiny part of the world anyway.
AAAAnd the answer is: That's just how it works :-).
Essentially it's because Factorio runs a lockstep simulation and every player needs
to have complete information about the whole map.
This has been discussed in slightly more detail in
[an older blogpost](https://www.factorio.com//blog/post/fff-76).

Now that we know that we really must copy all the data, the question is how to do
it well.

### Packets, TCP and elfs

As you probably already knew, the Internet works by passing small chunks of data
called [packets](https://en.wikipedia.org/wiki/Network_packet)
(or frames or segments or datagrams, depending on where exactly you look).
These are nothing special, just some bytes wrapped in an envelope made out of other bytes.
The network that these packets travel is a dangerous place, packet can get lost,
delayed, or even duplicated along the way.

To keep people sane and networks fast, someone came up with a way to hide all these
complications (because that's what programmers do) and invented the  TCP protocol.
Instead of transferring unreliable packets, it provides a stream of bytes
and the machinery inside wraps pieces of the stream to packets and makes sure that
all bytes arrive as sent.

Which sounds suspiciously similar to moving files over the network, right?
Just pop the whole thing into the stream and let the elfs do their magic.

### Differences between Factorio map transfers and UDP

Unfortunately in Factorio nothing is ever easy :-).
Using TCP would have three important consequences: It would force players to open
TCP ports as well as UDP ports in firewalls and NAT servers, it would remove the
possibility of using NAT punching now or later with MP matching server and it would
cause problems with downloading from multiple players at the same time.

So we decided to have file transfers done over UDP, even though it means reimplementing
a significant part of TCP functionality.

To make downloading from several other players possible in peer to peer mode,
the file transfers work by requesting individual blocks from peers and waiting for
the data, instead of sending data and waiting for acknowledgements.
Also we don't really care about ordering of the data blocks as long as all of them
are eventually transmitted.

### The story so far

When we started implementing the file transfer all I had was a rough idea of
"we're sending some packets and waiting for confirmations, somehow we know how many unconfirmed
packets there should be at one time and that value is called cWnd".
So Wikipedia came to the rescue and told me that a prety decent algorithm to determine
cWnd is called [CUBIC](https://en.wikipedia.org/wiki/CUBIC_TCP).
So I read an article or two, coded it up and ... it worked! Let's ship it!

A few releases, a few fixes, and a few desperate kludges later it worked very reliably
on my machine and the code got kind of forgotten.

About three weeks ago we decided to revisit the map transfers and I've spent two
fun weeks in TCP land.

### Congestion avoidance

The significant part of TCP functionality mentioned earlier is called congestion avoidance.

When you are sending some packets into the network you usually don't know in advance
how fast the network is or how long it will take to reach the other end and
if too many packets are stuffed into the connection not only some of them will be
dropped, but also packets belonging to other connections that go through the same
part of network will get dropped.
This situation is called network congestion and generally it's a thing we want to avoid,
because it slows the available transfer speed for everyone.

So the main idea behind congestion avoidance TCP is to send some amount data and
wait for acknowledgements before sending more.
If the acknowledgement arrives, we know that sending this amount of data is OK and
we try to send a little more next time, if on the other hand the packets start
disappearing we know we are sending too much and we should slow down.

Because of only a little complicated reasons, the increase of the window size
is a roughly linear function of time and the decrease is immediate halving, this
algorithm is called AIMD (additive increase, multiplicative decrease).
This way the amount of data in the network is oscillating between no congestion
at all and very minor congestion, slowly going up and then suddenly jumping down
to half speed.

### It's all in the details

Our first guess for the reasons of the slow transfer speeds was packet loss.
Both AIMD and CUBIC decrease the transfer speed drastically after a packet is lost
so when your microwave decides that the WiFi connection is its mortal enemy,
the congestion avoidance algorithm will slow down.

The measurements confirmed this, but compared to TCP, our file transfers were always
a little bit faster when packet loss was artificially added.

After an embarrassingly long period of experimentation I was left with two implementations.
AIMD, based mainly on [RFC 5681](https://tools.ietf.org/html/rfc5681) and
my own attempt slightly inspired by Sync-TCP and [LEDBAT](https://en.wikipedia.org/wiki/LEDBAT),
but other than that completely original.
And neither of them worked too well :-).

With my custom protocol that was not unexpected, it was mostly meant for experimenting
and to get a feeling for all the different behaviors the network can cause.

On the other hand why the AIMD implementation was not working was a mystery.
Finally after finding [RFC 2525](https://tools.ietf.org/html/rfc2525)
(thank you, internet people from 1999), I figured out that all problems in my life
were caused by a missing fast retransmit / fast recovery mechanism.

Fast retransmit and fast recovery work by marking a packet as lost not when its
timeout expires, but when three packets sent later are confirmed instead of the
packet in question.
In this case the packet is retransmitted and fast recovery mode is entered instead of waiting
a whole second, overflowing the network with extra packets the whole time.

After these changes we've upgraded the algorithm to be very similar to
[TCP Westwood](https://en.wikipedia.org/wiki/TCP_Westwood) which finally
gave us the improved behavior on lossy networks.

So TADAAA, here we are now.
The map transfer protocol is less tested than the previous one, but we feel more
certain about it, so let's hope that it's going to be the last change necessary
to this part of networking code.

### TL;DR

Map download was slow, cube spent some time doing computer stuff, now it should be faster. Let us know what you think [at the forum](https://forums.factorio.com/24658).
