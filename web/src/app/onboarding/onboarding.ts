import { ChangeDetectorRef, Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AccountInput, Currency, OnboardingState } from '../core/api.models';
import { ApiService } from '../core/api.service';
import { cardDebtIsValid, createAccountDraft, createCardDraft, formatMoney, normalizeDecimal, openingTotals, validAmount } from './draft';
import { OnboardingDraftController } from './draft-controller';
import { ButtonModule } from '@openng/optimus-ui/button';
import { InputTextModule } from '@openng/optimus-ui/inputtext';
import { SelectModule } from '@openng/optimus-ui/select';
import { MessageModule } from '@openng/optimus-ui/message';
@Component({selector:'app-onboarding',standalone:true,imports:[FormsModule,ButtonModule,InputTextModule,SelectModule,MessageModule],templateUrl:'./onboarding.html',styleUrl:'./onboarding.css'})
export class OnboardingComponent implements OnInit {
  @Input({required:true}) initialState!:OnboardingState; @Output() completed=new EventEmitter<OnboardingState>();
  readonly controller:OnboardingDraftController; readonly steps=['Empezamos','Cuentas','Tarjetas','Revisión'];
  readonly currencies:{label:string;value:Currency}[]=[{label:'Pesos argentinos · ARS',value:'ARS'},{label:'Dólares · USD',value:'USD'}];
  stage=0; confirming=false;
  constructor(api:ApiService,private readonly changeDetector:ChangeDetectorRef){this.controller=new OnboardingDraftController(api)}
  ngOnInit():void{this.controller.version=this.initialState.version;this.controller.completed=this.initialState.completed;this.controller.accounts=this.initialState.accounts.map(x=>({...x}));this.controller.cards=this.initialState.cards.map(x=>({...x}))}
  get totals():Record<Currency,string>{return openingTotals(this.controller.accounts)}
  get validForCompletion():boolean{return this.controller.accounts.every(a=>!!a.name.trim()&&!!a.bankName.trim()&&!!a.openingDate&&validAmount(a.openingBalance)&&a.openingBalance!=='')&&this.controller.cards.every(c=>!!c.name.trim()&&!!c.bankName.trim()&&!!c.asOfDate&&validDay(c.closingDay)&&validDay(c.dueDay)&&cardDebtIsValid(c)&&c.statementDebtArs!==''&&c.statementDebtUsd!=='')}
  async advance():Promise<void>{if(this.stage===0){this.stage=1;return}if(this.stage<3&&await this.controller.save())this.stage++;this.changeDetector.markForCheck()}
  back():void{if(this.stage>0)this.stage--}
  async skipAccounts():Promise<void>{this.controller.accounts=[];if(await this.controller.save())this.stage=2;this.changeDetector.markForCheck()}
  async skipCards():Promise<void>{this.controller.cards=[];if(await this.controller.save())this.stage=3;this.changeDetector.markForCheck()}
  async saveAndStay():Promise<void>{await this.controller.save();this.changeDetector.markForCheck()}
  async confirm():Promise<void>{this.confirming=true;try{if(await this.controller.confirm())this.completed.emit({version:this.controller.version,completed:true,accounts:this.controller.accounts,cards:this.controller.cards})}finally{this.confirming=false;this.changeDetector.markForCheck()}}
  addAccount():void{this.controller.accounts=[...this.controller.accounts,createAccountDraft()]}
  addCard():void{this.controller.cards=[...this.controller.cards,createCardDraft()]}
  removeAccount(id:string):void{this.controller.removeAccount(id)} removeCard(id:string):void{this.controller.removeCard(id)}
  amountLabel(raw:string,currency:Currency):string{return formatMoney(raw,currency)}
  accountId(account:AccountInput,field:string):string{return `${account.id}-${field}`}
  normalizeAccountAmount(account:AccountInput):void{account.openingBalance=normalizeDecimal(account.openingBalance)}
  normalizeCardAmount(card:{statementDebtArs:string;statementDebtUsd:string},field:'statementDebtArs'|'statementDebtUsd'):void{card[field]=normalizeDecimal(card[field])}
}

function validDay(day:number|null):boolean{return day===null||(Number.isInteger(day)&&day>=1&&day<=31)}
