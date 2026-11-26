-> ch3_ouadya_souq

=== ch3_ouadya_souq ===
#bg:butcher
#bgm:event:/BGM/CalmMusic

<i>Footsteps and talking</i>

I'm looking around the souq.

Someone nearby is talking.

#char:allal:neutral
#speaker:allal
<i>Laughs</i> Haaa Sidi, 5KG of lamb meat for your daughter's graduation! Mambroke 3likoum!

I notice Allal looking in my direction.

#char:allal:neutral
#speaker:allal
Huh? What do you want? What are you looking at?

#speaker:narrator
Si Allal! It's me, Abbass, Zohra's son.

#char:allal:neutral
#speaker:allal
Zohra? Zohra bent Abdelkader? Wife of Abdelkarim?

#speaker:narrator
Yes, that's the one!

#char:allal:neutral
#speaker:allal
Ya Allah... She was like my sister… Always helping me and the whole neighborhood, especially during Eid al-Adha.

#sfx:event:/SFX/Distortion

+ [She was kind to everyone…]
    -> allal_memories

+ [She is kind to everyone…] #denial: 1
#glitch:0.5
    -> allal_memories

=== allal_memories ===
#char:allal:neutral
#speaker:allal
Your father, Abdelkarim, came to my shop every week. That's how he met her, right here at this counter. The next thing I knew, he was asking me how to propose.

After they got married, they left the Oudayas. She never came back after that.

#sfx:event:/SFX/Distortion

+ [She always wanted me to visit here.]
    -> allal_reunion

+ [She will come back when she gets better.] #denial: 1
#glitch:0.5
    -> allal_reunion

=== allal_reunion ===
#char:allal:neutral
#speaker:allal
I can see her eyes in you. I'm glad you came, a wlidi. It feels like she's here with us.

#sfx:event:/SFX/Distortion

+ [I should have come here with her.]
    -> allal_farewell

+ [I will come here with her once she feels better…] #denial: 1
#glitch:0.5
    -> allal_farewell

=== allal_farewell ===
-> END