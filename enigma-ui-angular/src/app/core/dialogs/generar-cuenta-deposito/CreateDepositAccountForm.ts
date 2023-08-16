import { Divisa } from './../../model/divisa';
import { FormControl } from '@angular/forms';
import { Pais } from '../../model/pais';
import { TipoCuentaDeposito } from '../../model/TipoCuentaDeposito';


export interface CreateDepositAccountForm {
  AccountAlias: FormControl<string>;
  Description: FormControl<string>;
  InitialFunds: FormControl<number>;
  Country: FormControl<Pais>;
  Currency: FormControl<Divisa>;
  Type: FormControl<TipoCuentaDeposito>;
}
