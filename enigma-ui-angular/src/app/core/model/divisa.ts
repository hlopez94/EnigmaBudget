import { Pais } from "./pais";

export interface Divisa {
  code:string
  num:number
  name:string
  country:Pais;
  exponent:number;
}
