class Bots implements IPage {
	private divBots!: HTMLElement;
	private bots: { [key: string]: IPlusInfo } = {};

	public async init() {
		this.divBots = Util.getElementByIdSafe("bots");
		await this.refresh();
	}

	public async refresh() {
		const res = await jmerge(
			cmd<CmdBotInfo[]>("bot", "list"),
			cmd<{ [key: string]: CmdBotsSettings }>("settings", "global", "get", "bots"),
		).get();

		if (!DisplayError.check(res, "Error getting bot list"))
			return;

		Util.clearChildren(this.divBots);
		this.bots = {};

		for (const botInfo of res[0]) {
			let bot: IPlusInfo = botInfo as IPlusInfo;
			bot.Running = true;
			this.bots[botInfo.Name] = bot;
		}

		const botSettList = res[1];
		for (const botName in botSettList) {
			let bot = this.bots[botName];
			if (bot === undefined) {
				bot = this.bots[botName] = {
					Name: botName,
					Running: false,
				}
			}
			bot.Autostart = botSettList[botName].run;
		}

		for (const botInfoName in this.bots) {
			this.refreshBot(this.bots[botInfoName]);
		}
	}

	public refreshBot(botInfo: IPlusInfo) {
		const botCard = this.botCard(botInfo);
		if (botCard !== undefined) {
			let oldInfo = this.bots[botInfo.Name];
			if (oldInfo !== undefined && oldInfo.Div !== undefined) {
				const oldDiv = oldInfo.Div;
				this.divBots.replaceChild(botCard, oldDiv);
			} else {
				this.divBots.appendChild(botCard);
			}
			botInfo.Div = botCard;
		}
		Main.generateLinks();
	}

	private botCard(botInfo: IPlusInfo): HTMLElement | undefined {
		let divStartStopButton: IJsxGet = {};
		let div = <div class={"botCard formbox" + (botInfo.Running ? " botRunning" : "")}>
			<div class="formheader flex2">
				<div>{botInfo.Name}</div>
				<div when={botInfo.Id !== undefined}>
					[ID:{botInfo.Id}]
				</div>
			</div>
			<div class="formcontent">
				<div class="formdatablock">
					<div>Server:</div>
					<div>{botInfo.Server}</div>
				</div>
				{/* <div class="formdatablock">
					<div>Autostart:</div>
					<div><input type="checkbox" value="Autostart" /></div>
				</div> */}
				<div class="flex2">
					<div>
						<a when={botInfo.Running} class="jslink button buttonMedium buttonIcon"
							href={"index.html?page=bot.html&bot_id=" + botInfo.Id}
							style="background-image: url(media/icons/list-rich.svg)"></a>
					</div>
					<div class={"button buttonRound buttonMedium buttonIcon " + (botInfo.Running ? "buttonRed" : "buttonGreen")}
						set={divStartStopButton}
						style={"background-image: url(media/icons/" + (botInfo.Running ? "power-standby" : "play-circle") + ".svg)"}>
					</div>
				</div>
			</div>
		</div>;

		if (divStartStopButton.element !== undefined) {
			const divSs = divStartStopButton.element;
			divSs.onclick = async (_) => {
				Util.setIcon(divSs, "cog-work");
				divSs.style.color = "transparent";
				if (!botInfo.Running) {
					const res = await cmd<CmdBotInfo>("bot", "connect", "template", botInfo.Name).get();
					if (!DisplayError.check(res, "Error starting bot")) {
						Util.clearIcon(divSs);
						divSs.style.color = null;
						return;
					}
					Object.assign(botInfo, res);
					botInfo.Running = true;
				} else {
					const res = await bot(cmd("bot", "disconnect"), botInfo.Id).get();
					if (!DisplayError.check(res, "Error stopping bot")) {
						Util.clearIcon(divSs);
						divSs.style.color = null;
						return;
					}
					botInfo.Id = undefined;
					botInfo.Server = undefined;
					botInfo.Running = false;
				}
				this.refreshBot(botInfo);
			};
		}

		return div;
	}
}

type IPlusInfo = Partial<CmdBotInfo> & {
	Name: string;
	Running: boolean;
	Autostart?: boolean;
	Div?: HTMLElement;
};
