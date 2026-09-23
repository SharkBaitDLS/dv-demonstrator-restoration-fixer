## Description

This is a mod for Derail Valley that recovers demonstrator locomotives and their restoration quests from
an earlier save, after a railway change or a bug has reset them. For details, see the mod page on Nexus
Mods.

### What goes wrong

Derail Valley files a save's rolling stock under a hash of the railway it was parked on (`Cars#<hash>` in
the save). Change the railway — a game update, or installing or removing a track mod such as Double Track
— and the game no longer finds a car list for the railway it now has, so nothing spawns.

The demonstrators go down with everything else, but they fail louder. Each `LocoRestorationController`
looks up its locomotive by GUID, doesn't find it, writes the demonstrator off and drops a fresh wreck in
its place at state 0. A restored S282 with a custom paint job and a cab full of gadgets becomes an
abandoned hulk again. Save once after that and the reset is the save's own state.

### What this mod does

It adds a page to the Unity Mod Manager settings that lists every save and backup belonging to the session
you are playing, reads the demonstrator records out of the one you pick, and shows them against what the
loaded save holds and what the world is actually doing right now — which are not always the same thing.

Choose the demonstrators to bring back and press the button. Each one's stand-in wreck is taken out of the
world and its real locomotive is put back where it stood, then its `LocoRestorationController` is handed
the record from the save you picked and carries on from there. That is the same `LoadData` the game calls
at the end of a load, so every state from an untouched wreck to a finished paint job comes back the way the
game itself builds it — no reload needed.

Restored with the locomotive:

- restoration quest state, up to and including a part on order
- exterior and interior paint themes
- installed gadgets, and loose items that were in the cab
- damage, wear, simulation state and drilled mounting holes
- loaded cargo, handbrake and air system pressures
- couplings and air hoses between the locomotive and its tender, and to any other demonstrator restored
  alongside it that it stood coupled to

Where it ends up depends on the railway. On the same railway a locomotive goes back exactly where it
stood. Once the railway has changed its old track references mean nothing, so it is set down on the
nearest piece of track with room for it, near where it used to be, facing the way it did — this is the
same thing the game itself does for the caboose. Cars that stood coupled together, such as a locomotive
and its tender or one demonstrator towing another restored at the same time, go back as the train they
were — laid out by the game's own train spawning, in the same order and still coupled, on the nearest
track with room for all of them. Couplings to anything that isn't being restored with them are let go,
with the air cock on that end closed so the brakes aren't left venting.

A finished demonstrator is also its garage's car, which the game only ties together at the moment a
restoration completes. Restoring one hands it back to its garage and unlocks it; winding one back below
that point takes the unlock away again, so the garage can't quietly spawn a replacement for a locomotive
that is no longer finished.

### Custom Demonstrators

If the save being restored from carries data from the
[Custom Demonstrators](https://github.com/SharkBaitDLS/dv-custom-demonstrators) mod, what it recorded about
the demonstrators being restored — how each was configured, where its slot stands, which parts cargo it was
given — comes back with them.

That record describes every demonstrator at once, while a restore puts back only the ones you picked, so it
is rebuilt rather than copied over: the demonstrators being restored take their entry from the save they
came from, and every other slot keeps what this save already said about it. What the record ends up
describing is the world the restore actually produces, so the two never disagree about what is standing
where. Custom Demonstrators owns that format, so it is the one asked to do the rebuilding, and what it says
about ordinary work train garages is left alone — this mod doesn't move their cars.

Custom Demonstrators otherwise only reads its record while a save is loading, so this mod asks it to read it
again once the restored entries are in, and it picks them up straight away rather than at the next load.
That mod's own settings are left alone: if they disagree with the record, its save guard holds them back and
plays the record instead, exactly as it would for any save it didn't build. This all happens before the
locomotives are put back, so that each garage is already set up for the stock the old save gave it by the
time a demonstrator is handed to it.

The demonstrator slots themselves come back the same way. If a restored entry names a slot Custom
Demonstrators added and this session never built, that mod builds it when it reads the record, and the
locomotive goes back into it in the same breath; a slot no restored entry names any more is taken apart. So
a save restored from one with more or fewer demonstrators than the current one comes back with the ones it
had.

A locomotive going back into the slot it came from displaces whatever stands there now. Usually that costs
nothing — after a railway change every slot has been reset to a fresh wreck. Where it would cost something,
the comparison says so and offers the alternative: Custom Demonstrators can stand the returning locomotive
in a demonstrator slot of its own, leaving the one already there untouched, so you keep both. It is offered
only once the demonstrator being displaced has been rerailed, since everything before that follows from
licenses you already own and costs nothing to do again.

A locomotive brought back that way is not put back exactly as it was, and the comparison says how: it
stands in a free museum stall rather than the one it had, and its restoration quest asks for parts cargo
of its own.

The parts cargo numbers are merged too, for a different reason. Custom Demonstrators gives each demonstrator's
restoration parts a cargo number of their own and keeps which one went where in the save, and a car carrying
a part records the number rather than the demonstrator. Handing the save an older copy of that list would
free a number a car in the world is still holding, and the next load would be at liberty to give it to
somebody else's parts. So the list is merged: a number a loaded car is carrying stays where it is, and
anything else is free to move. Every crate standing in the world goes on meaning what it meant.

With Custom Demonstrators absent, or too old to be asked, nothing here can rebuild its record, so the
selected save's copy of it travels with this save unchanged, to be read the next time it loads. Restoring
only some of that save's demonstrators says so, since a record carried over whole describes the others too.

### Scope and safety

Nothing is written to disk. The restore changes the world and the save held in memory, so if the result
isn't what you wanted, quit to the main menu without saving and the save on disk is exactly as it was.

Only demonstrators are recovered. A railway change orphans the rest of a save's rolling stock and jobs
too, and putting those back is a different problem: their positions, consists and job tracks are tied to a
railway that no longer exists.

Items are only ever moved, never conjured. A gadget is put back on its locomotive only if the matching
item is sitting unattached in lost and found, which is where the game sweeps it when the car it was
bolted to fails to load. Anything that can't be accounted for is reported rather than duplicated.

The same goes the other way. Gadgets fitted to a locomotive that a restore takes out of the world are moved
to lost and found as it goes, rather than being left lying on the ground where it stood.

A restoration whose part was already in transit is the one thing deliberately not put back as it was. The
only thing that can carry a part is an ordinary cargo car, and that car belongs to the world rather than to
the demonstrator: by the time you come to restore, it has most likely been hauled off on a job, emptied, or
replaced by a garage that spawned a fresh one, and the delivery it was filling was voided when the
demonstrator was written off. Putting that back would mean either spawning a second copy of a flatcar
somebody is using or reaching into the cargo on one that is in service, to save a single haul. So the
restoration comes back one step earlier instead, with the part on order and ready to be collected again.

Going the other way, a demonstrator standing here with a part already in transit has that part returned to
the warehouse it came from as its restoration is wound back, and the car that was hauling it is handed back
to you empty. The alternative is a crate nothing can ever clear: the museum's own flatcar is a garage car,
which the game's stray car sweeper will not touch, and cargo that no job is asking for has no other way
off.

## Building

Building the project requires some initial setup, after which running `dotnet build` will do a Debug build or running `dotnet build -c Release` will do a Release build.

### References Setup

After cloning the repository, some setup is required in order to successfully build the mod DLLs. You will need to create a new [Directory.Build.targets][references-url] file to specify your local reference paths. This file will be located in the main directory, next to DRF.sln.

Below is an example of the necessary structure. When creating your targets file, you will need to replace the reference paths with the corresponding folders on your system. Make sure to include semicolons **between** each of the paths and no semicolon after the last path. Also note that any shortcuts you might use in file explorer—such as %ProgramFiles%—won't be expanded in these paths. You have to use full, absolute paths.
```xml
<Project>
	<PropertyGroup>
		<ReferencePath>
			C:\Program Files (x86)\Steam\steamapps\common\Derail Valley\DerailValley_Data\Managed\
		</ReferencePath>
		<AssemblySearchPaths>
			$(AssemblySearchPaths);$(ReferencePath)
		</AssemblySearchPaths>
	</PropertyGroup>
</Project>
```

## Packaging

To package a build for distribution, you can run the `package.ps1` PowerShell script in the root of the project. If no parameters are supplied, it will create a .zip file ready for distribution in the dist directory. A post build event is configured to run this automatically after each successful Release build.

Linux: `pwsh ./package.ps1`
Windows: `powershell -executionpolicy bypass .\package.ps1`

### Parameters

Some parameters are available for the packaging script.

#### -NoArchive

Leave the package contents uncompressed in the output directory.

#### -OutputDirectory

Specify a different output directory.
For instance, this can be used in conjunction with `-NoArchive` to copy the mod files into your Derail Valley installation directory.

## License

Source code is distributed under the MIT license.
See [LICENSE][license-url] for more information.

[license-url]: https://github.com/SharkBaitDLS/dv-demonstrator-restoration-fixer/blob/main/LICENSE
[references-url]: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022
