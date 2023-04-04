import { Pais } from "./pais";

export interface Moneda {
  code:string
  num:number
  name:string
  country:Pais;
  exponent:number;
}
