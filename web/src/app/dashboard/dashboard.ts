import { Component, Input } from '@angular/core';
import { AccountSummary, CardSummary, Currency } from '../core/api.models';
import { formatMoney, openingTotals } from '../onboarding/draft';
@Component({selector:'app-dashboard',standalone:true,templateUrl:'./dashboard.html',styleUrl:'./dashboard.css'})
export class DashboardComponent {
  @Input() name=''; @Input() accounts:AccountSummary[]=[]; @Input() cards:CardSummary[]=[];
  get totals():Record<Currency,string>{return openingTotals(this.accounts)}
  money(amount:string,currency:Currency):string{return formatMoney(amount,currency)}
  countCurrency(currency:Currency):number{return this.accounts.filter(x=>x.currency===currency).length}
  dateLabel(date:string):string{return new Intl.DateTimeFormat('es-AR',{dateStyle:'medium',timeZone:'UTC'}).format(new Date(`${date}T00:00:00Z`))}
}
