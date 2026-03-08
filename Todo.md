To-add list:
I think most errors need to use toasts instead of just logging to the console.
I need to personally double-check all the messages because I'm sure they're full of lies and can be ten million times clearer.
Make being informative not an afterthought. Assume the user is a high school dropout. They don't know about statistical significance, so we should explain that when they're setting up the eval inputs. They don't know how attention mechanics tend to be trained to perform, so we should tell them instructions belong at the start/end/both ends of the prompt, especially for the judge. Maybe they don't even know that you can use XML tags to make it easier to refer to large sections of your input in your instructions, or that you should generally spell everything correctly for maximum quality, or that no LLM capability/quality necessarily generalizes outside the test set, or that lower temperature/higher min-P or top-P (which is probably worse than min-P) may improve the chances of correct output.
Then do a major polish pass, like changing the order of the settings. For example, Parallel Slots should be to the right of Context Window, not below Batch Size.
Various pipeline setups:
	For the CasualQA pipeline, optimize the prompts and drop the pointless pass/fail part of the judge prompt.
	For coding chat, take code and instructions as input, potentially a code-changes-applying model run (like MS NextCoder with code-patch speculative decoding), accept entire projects to plug the updated code into, accept commands to compile and run unit tests on it, and then LLM-as-judge after compilation with a rubric that might include things like subjective code quality metrics, completeness, relative importance of the individual tests in case some failed, maybe even fixability in case it couldn't compile--and so maybe an auto-repair stage would also be good.
	For translation, take one language as input and the same thing in another language as the output (maybe even add a feature to generate an eval set using a much larger model), and judge with the original, expected output, and real output plus a rubric that covers technical accuracy (it said the same thing), mood accuracy (how it was translated gives the same feeling as how it was said), naturalness, and presence of notes when cultural context is needed.
		Can even have a translation variant for multi-input and structured output, like how https://github.com/dpmm99/MNN-Android-Interpreted-Chat-Server/blob/master/apps/Android/MnnLlmChat/app/src/main/java/com/alibaba/mnnllm/android/hotspot/TranslationManager.kt#L297 auto-translates UI elements.
	For judgment, another eval's input-output would be the input and that eval's judge output (or a human's judgment) would be the expected output--you can use statistical approaches, like R^2 score, to determine how well the judge matches your own opinion or another model's evaluation.
	And, of course, I need to fine-tune any built-in prompts to try to maximize their effectiveness.
	Also ought to include some simple examples of data that you can run evals on (but real ones, not the minimalist garbage I got from a single Qwen3.5-122B-A10B prompt), with the disclaimer that the more widely posted a problem is, the less you can trust that evaluating an LLM on that problem will generalize to real-world use.

To-fix list:
There are some pieces of code trying to show toasts, but no toasts ever appear in the UI.
There's probably a lot of code simplification that can be done thanks to leftover artifacts from the agent grasping at straws while trying to fix bugs.
The browse buttons in the settings view don't populate the backing fields or the textboxes.
Normalizing the judge score is inappropriate and leads to things like the UI saying "1.0/10" when the judge gave 10/10.

To-test list:
Continuation of an interrupted run
