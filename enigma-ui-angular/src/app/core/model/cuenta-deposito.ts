import { TipoCuentaDeposito } from './TipoCuentaDeposito';
import { Divisa } from './divisa';
import { Pais } from './pais';

export interface CuentaDeposito {
  id: number;
  description: string;
  funds: number;
  country: Pais;
  currency: Divisa;
  type: TipoCuentaDeposito;
}
