import { TipoCuentaDeposito } from './TipoCuentaDeposito';
import { Divisa } from './divisa';
import { Pais } from './pais';

export interface CuentaDeposito {
  id: string;
  description: string;
  name:string;
  funds: number;
  country: Pais;
  currency: Divisa;
  type: TipoCuentaDeposito;
}
