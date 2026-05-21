# Configuration

You can configure AurSh to your liking. Whether you want your prompt 
to be a verbose 2 line prompt or just a simple 1 line.

You can configure by editing a file in `.aursh/AurSh.config.con`.

```AurSh.config.con

[Config]

PromptSpacing=<Sparse (1 space in between prompts: prompt-space-output-space-prompt) or Compressed(no space: meaining prompt-output-prompt), default=Compressed> : space in between prompts

PromptLine=<dotted,line,none, default=none> : changes the line that connects the time segment to the main segment in the prompt.

SegmentEdges=<angled,arrow,rounded, default=arrow> : change the segment edges in the prompt UI.

PromptTheme=<custom themes> : Changes the Color theme of the prompt (light,dark and bright)
Verbose=<true/false, default=true> : Changes if the prompt is 2 or just 1 line.

```

